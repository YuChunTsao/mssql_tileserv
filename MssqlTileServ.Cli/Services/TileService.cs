using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Types;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Utils;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;

namespace MssqlTileServ.Cli.Services;

public class TileService
{
    private readonly string _connectionString;
    private const int EPSG_4326 = 4326;

    public TileService(string connectionString)
    {
        _connectionString = connectionString;
    }

    private string PrepareSqlQuery(Config config, Envelope bounds, Envelope bufferedBounds, LayerMeta layerMeta)
    {
        string layername = layerMeta.Name;
        string geometryColumnName = layerMeta.GeometryColumnName;
        int SRID = layerMeta.SRID[0];

        // FIXME: Now, it will return two geometries: the original geometry and the intersection with the buffered bounds.
        // Pass columns to the query to avoid returning the same geometry twice.
        return $@"
            SELECT
                *,
                {geometryColumnName}.STIntersection(
                    geometry::STGeomFromText(
                        'POLYGON((
                            {bufferedBounds.MinX} {bufferedBounds.MaxY},
                            {bufferedBounds.MaxX} {bufferedBounds.MaxY},
                            {bufferedBounds.MaxX} {bufferedBounds.MinY},
                            {bufferedBounds.MinX} {bufferedBounds.MinY},
                            {bufferedBounds.MinX} {bufferedBounds.MaxY}
                        ))', {SRID}
                    )
                ) AS {geometryColumnName}
            FROM {config.Database.Name}.{config.Database.Schema}.{layername}
            WHERE {geometryColumnName}.STIntersects(
                geometry::STGeomFromText(
                    'POLYGON((
                        {bounds.MinX} {bounds.MaxY},
                        {bounds.MaxX} {bounds.MaxY},
                        {bounds.MaxX} {bounds.MinY},
                        {bounds.MinX} {bounds.MinY},
                        {bounds.MinX} {bounds.MaxY}
                    ))', {SRID}
                )
            ) = 1
          ";
    }

    // TODO: Maybe we should use NetTopologySuite.IO.SqlServerBytes to handle the conversion more efficiently
    private Task<TileData> GetTileData(Config config, string sqlQuery)
    {
        TileData tileData = new TileData();

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
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
        }

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

    private Layer CreateVectorTileLayer(string layername, TileData tileData)
    {
        List<IFeature> features = TileDataToFeatures(tileData);

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

    public VectorTile GetVectorTile(Config config, LayerMeta layerMeta, int z, int x, int y)
    {
        Envelope bounds = TileHelper.TileIdToBounds(x, y, z);
        Envelope bufferedBounds = TileHelper.TileIdToBounds(x, y, z, config.Tile.Extent, config.Tile.Buffer);

        if (layerMeta.SRID != null && layerMeta.SRID.Length > 0 && layerMeta.SRID[0] != EPSG_4326)
        {
            // Convert the bounds to a geometry
            GeometryFactory geometryFactory = new GeometryFactory(new PrecisionModel(), EPSG_4326);
            Geometry boundsGeometry = geometryFactory.ToGeometry(bounds);

            // Project the bounds to the layer's SRID
            boundsGeometry = boundsGeometry.ProjectTo(layerMeta.SRID[0]);

            // Update the bounds with the projected geometry
            bounds = boundsGeometry.EnvelopeInternal;

            Geometry bufferedBoundsGeometry = geometryFactory.ToGeometry(bufferedBounds);
            bufferedBoundsGeometry = bufferedBoundsGeometry.ProjectTo(layerMeta.SRID[0]);
            bufferedBounds = bufferedBoundsGeometry.EnvelopeInternal;
        }

        var tileDefinition = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x, y, z);
        VectorTile vectorTile = new VectorTile { TileId = tileDefinition.Id };

        string layername = layerMeta.Name;
        string sqlQuery = PrepareSqlQuery(config, bounds, bufferedBounds, layerMeta);
        TileData tileData = GetTileData(config, sqlQuery).GetAwaiter().GetResult();

        // If the projection of the layer is not WGS84, we need to transform the geometries in the tileData
        if (layerMeta.SRID != null && layerMeta.SRID.Length > 0 && layerMeta.SRID[0] != EPSG_4326)
        {
            for (int i = 0; i < tileData.Geometries.Count; i++)
            {
                Geometry geometry = new WKBReader().Read(tileData.Geometries[i]);
                geometry.SRID = layerMeta.SRID[0];
                geometry = geometry.ProjectTo(EPSG_4326);
                tileData.Geometries[i] = new WKBWriter().Write(geometry);
            }
        }

        Layer layer = CreateVectorTileLayer(layername, tileData);
        vectorTile.Layers.Add(layer);

        return vectorTile;
    }

    // TODO: Support multiple layers in a single tile
    public byte[] GetVectorTileBytes(Config config, LayerMeta layerMeta, int z, int x, int y)
    {
        VectorTile vt = GetVectorTile(config, layerMeta, z, x, y);
        byte[] tile;
        using (var ms = new MemoryStream())
        {
            vt.Write(ms, MapboxTileWriter.DefaultMinLinealExtent, MapboxTileWriter.DefaultMinPolygonalExtent);
            tile = ms.ToArray();
        }

        tile = CompressMVT(tile);

        return tile;
    }

    public static List<LayerMeta> GetAvailableTables(string connectionString)
    {
        List<LayerMeta> layers = new List<LayerMeta>();

        string sql_find_table_info = """
        SELECT
            o.name AS ObjectName,
            o.type AS ObjectType,
            c.name AS ColumnName,
            ty.name AS TypeName
        FROM
            sys.columns c
        JOIN
            sys.objects o ON c.object_id = o.object_id
        JOIN
            sys.types ty ON c.user_type_id = ty.user_type_id
        WHERE
            o.type IN ('U', 'V') -- U: Table, V: View
            AND ty.name IN ('geometry', 'geography');
        """;

        // SQL Server allows users to store geometries with different SRIDs in the same geometry column.
        string sql_find_srid = """
          SELECT
            {geometryColumnName}.STSrid AS SRID
          FROM
            {tableName}
          WHERE
            {geometryColumnName} IS NOT NULL
        """;

        string sql_find_spatial_index = """
        SELECT
            t.name AS table_name
        FROM
            sys.tables t
        JOIN
            sys.schemas s ON t.schema_id = s.schema_id
        JOIN
            sys.indexes i ON i.object_id = t.object_id AND i.type_desc = 'SPATIAL'
        """;

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql_find_table_info;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var objectName = reader.GetString(reader.GetOrdinal("ObjectName"));
                        var objectType = reader.GetString(reader.GetOrdinal("ObjectType"));
                        var columnName = reader.GetString(reader.GetOrdinal("ColumnName"));
                        var typeName = reader.GetString(reader.GetOrdinal("TypeName"));

                        // TODO: Only get the first geometry column (?)

                        // Check if the layer already exists
                        var existingLayer = layers.FirstOrDefault(l => l.Name == objectName);
                        if (existingLayer == null)
                        {
                            // Create a new layer meta
                            layers.Add(new LayerMeta
                            {
                                Name = objectName,
                                ObjectType = objectType,
                                GeometryColumnName = columnName,
                                GeometryTypeName = typeName
                            });
                        }
                        else
                        {
                            // Update the existing layer with the geometry column and type
                            existingLayer.GeometryColumnName = columnName;
                            existingLayer.GeometryTypeName = typeName;
                        }
                    }
                }
            }
        }

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                // Query the SRID for each layer
                command.CommandText = sql_find_srid;
                foreach (var layer in layers)
                {
                    command.CommandText = sql_find_srid
                        .Replace("{geometryColumnName}", layer.GeometryColumnName)
                        .Replace("{tableName}", layer.Name);
                    using (var sridReader = command.ExecuteReader())
                    {
                        List<int> srids = new List<int>();
                        while (sridReader.Read())
                        {
                            if (!sridReader.IsDBNull(0))
                            {
                                srids.Add(sridReader.GetInt32(0));
                            }
                        }
                        layer.SRID = srids.Distinct().ToArray(); // Store distinct SRIDs
                    }
                }
            }
        }

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                // Query the spatial index for each layer
                command.CommandText = sql_find_spatial_index;
                using (var spatialIndexReader = command.ExecuteReader())
                {
                    while (spatialIndexReader.Read())
                    {
                        var tableName = spatialIndexReader.GetString(spatialIndexReader.GetOrdinal("table_name"));
                        var layer = layers.FirstOrDefault(l => l.Name == tableName);
                        if (layer != null)
                        {
                            layer.HasSpatialIndex = true;
                        }
                    }
                }
            }
        }

        return layers;
    }
}
