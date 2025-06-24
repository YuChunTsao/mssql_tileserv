using System.Data;
using Microsoft.SqlServer.Types;
using MssqlTileServ.Cli.Models;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using NetTopologySuite.Precision;

namespace MssqlTileServ.Cli.Services;

public class TileService
{
    private readonly IDbConnection _connection;

    public TileService(IDbConnection connection)
    {
        _connection = connection;
    }

    private string PrepareSqlQuery(Config config, Envelope bounds, string layername)
    {
        // TODO: We have to know the column name of the geometry column
        // For now, we assume the geometry column is named 'geometry'
        return $@"
            SELECT *
            FROM {config.Database.Name}.{config.Database.Schema}.{layername}
            WHERE geometry.STIntersects(
                geometry::STGeomFromText(
                    'POLYGON((
                        {bounds.MinX} {bounds.MaxY},
                        {bounds.MaxX} {bounds.MaxY},
                        {bounds.MaxX} {bounds.MinY},
                        {bounds.MinX} {bounds.MinY},
                        {bounds.MinX} {bounds.MaxY}
                    ))', 4326
                )
            ) = 1
          ";
    }

    private Task<TileData> GetTileData(Config config, Envelope bounds, string layername)
    {
        // TODO: call TileIdToBounds? don't pass bounds as a parameter
        // pass z/x/y as parameters instead
        string sqlQuery = PrepareSqlQuery(config, bounds, layername);

        TileData tileData = new TileData();

        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = sqlQuery;
            command.CommandTimeout = config.Database?.DbTimeout ?? 10;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var attributes = new Dictionary<string, object?>();
                    byte[]? geometry = null;

                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        // Check if the column is of type SqlGeometry or SqlGeography
                        if (reader.GetFieldType(i) == typeof(SqlGeometry) || reader.GetFieldType(i) == typeof(SqlGeography))
                        {
                            if (reader[i] is SqlGeometry sqlGeom)
                            {
                                // Convert SqlGeometry to WKB
                                geometry = sqlGeom.STAsBinary().Value;
                            }
                            else if (reader[i] is SqlGeography sqlGeog)
                            {
                                // Convert SqlGeography to WKB
                                geometry = sqlGeog.STAsBinary().Value;
                            }
                        }
                        else
                        {
                            var columnName = reader.GetName(i);
                            attributes[columnName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                    }

                    if (geometry != null)
                    {
                        tileData.Geometries.Add(geometry);
                        tileData.Attributes.Add(attributes);
                    }
                }

            }
        }
        _connection.Close();

        return Task.FromResult(tileData);
    }

    private List<IFeature> TileDataToFeatures(TileData tileData)
    {
        List<IFeature> features = new List<IFeature>();

        WKBReader wkbReader = new WKBReader();

        for (int i = 0; i < tileData.Geometries.Count; i++)
        {
            Geometry geometry = wkbReader.Read(tileData.Geometries[i]);
            Dictionary<string, object?> attributesDict = tileData.Attributes[i];

            AttributesTable attributesTable = new AttributesTable();
            foreach (var kvp in attributesDict)
            {
                attributesTable.Add(kvp.Key, kvp.Value);
            }

            features.Add(new Feature(geometry, attributesTable));
        }

        return features;
    }

    // TODO: pass config as a parameter
    private Layer CreateVectorTileLayer(string layername, TileData tileData, Envelope bounds)
    {
        // TODO:
        // Now, we suppose that the projection is WGS84 (EPSG:4326)
        // I'll Implement a method to handle different projections later

        List<IFeature> features = TileDataToFeatures(tileData);

        // Clip the features with the buffered bounds
        PrecisionModel precisionModel = new PrecisionModel(1e6);
        GeometryFactory geometryFactory = new GeometryFactory(precisionModel);
        GeometryPrecisionReducer reducer = new GeometryPrecisionReducer(precisionModel);
        Geometry bufferedBounds = geometryFactory.ToGeometry(bounds);
        var reducedTile = reducer.Reduce(bufferedBounds);

        // Iterate through the features and reduce their geometries
        for (int i = 0; i < features.Count; i++)
        {
            // If the geometry is point or multipoint, we can skip the reduction
            if (features[i].Geometry is Point || features[i].Geometry is MultiPoint)
            {
                continue;
            }

            var reducedGeometry = reducer.Reduce(features[i].Geometry);

            // Clip the feature geometry to the tile bounds
            Geometry clippedGeometry = reducedGeometry.Intersection(reducedTile);
            if (clippedGeometry.IsEmpty)
            {
                features.RemoveAt(i);
                i--; // Adjust index after removal
                continue;
            }
            else
            {
                // Update the feature with the clipped geometry
                features[i] = new Feature(clippedGeometry, features[i].Attributes);
            }
        }

        // TODO: Simplify the geometries if needed

        Layer layer = new Layer()
        {
            Name = layername,
        };

        foreach (var feature in features)
        {
            layer.Features.Add(feature);
        }

        return layer;
    }

    private byte[] CompressMVT(byte[] data)
    {
        using (var outputStream = new MemoryStream())
        {
            using (var gzipStream = new System.IO.Compression.GZipStream(outputStream, System.IO.Compression.CompressionLevel.Optimal))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
    }

    public VectorTile GetVectorTile(Config config, string layername, int z, int x, int y)
    {
        Envelope bounds = TileHelper.TileIdToBounds(x, y, z);
        Envelope bufferedBounds = TileHelper.TileIdToBounds(x, y, z, config.Tile.Extent, config.Tile.Buffer);

        var tileDefinition = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x, y, z);
        VectorTile vectorTile = new VectorTile { TileId = tileDefinition.Id };

        TileData tileData = GetTileData(config, bounds, layername).GetAwaiter().GetResult();
        Layer layer = CreateVectorTileLayer(layername, tileData, bufferedBounds);
        vectorTile.Layers.Add(layer);

        return vectorTile;
    }

    public byte[] GetVectorTileBytes(Config config, string layername, int z, int x, int y)
    {
        VectorTile vt = GetVectorTile(config, layername, z, x, y);
        byte[] tile;
        using (var ms = new MemoryStream())
        {
            vt.Write(ms, MapboxTileWriter.DefaultMinLinealExtent, MapboxTileWriter.DefaultMinPolygonalExtent);
            tile = ms.ToArray();
        }

        tile = CompressMVT(tile);

        return tile;
    }
}
