using System.Data;
using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Types;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Utils;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using Serilog;

namespace MssqlTileServ.Cli.Services;

public class TileService
{
    private readonly string _connectionString;
    private readonly ILogger<TileService> _logger;
    private const int EPSG_4326 = 4326;

    public TileService(string connectionString, ILogger<TileService> logger)
    {
        _logger = logger;
        _logger.LogInformation("TileService instance created");
        _connectionString = connectionString;
    }

    private async Task<TileData> GetTileData(Config config, Geometry boundsGeometry, Geometry bufferedBoundsGeometry, LayerMeta layerMeta)
    {
        string layername = layerMeta.Name;
        string geometryColumnName = layerMeta.GeometryColumnName;
        int SRID = layerMeta.SRID;

        _logger.LogDebug("Getting tile data for layer {LayerName} with SRID {SRID}", layername, SRID);

        var columnList = string.Join(", ", layerMeta.Columns.Where(c => !string.Equals(c, geometryColumnName, StringComparison.OrdinalIgnoreCase))
            .Select(c => $"[{c}]"));

        string sqlQuery =
        $@"
          SELECT
              {columnList},
              {geometryColumnName}.STIntersection(@bufferedBoundsGeometry) AS {geometryColumnName}
          FROM {config.Database.Name}.{config.Database.Schema}.{layername}
          WHERE {geometryColumnName}.STIntersects(@boundsGeometry) = 1
        ";

        _logger.LogTrace("Executing SQL query for layer {LayerName}: {SqlQuery}", layername, sqlQuery);

        TileData tileData = new TileData();
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                var geometryWriter = new SqlServerBytesWriter { IsGeography = layerMeta.GeometryTypeName == "geography" };
                var parameter = command.Parameters
                    .AddWithValue("boundsGeometry", new SqlBytes(geometryWriter.Write(boundsGeometry)));
                parameter = command.Parameters
                    .AddWithValue("bufferedBoundsGeometry", new SqlBytes(geometryWriter.Write(bufferedBoundsGeometry)));

                parameter.SqlDbType = SqlDbType.Udt;
                parameter.UdtTypeName = layerMeta.GeometryTypeName;

                command.CommandText = sqlQuery;
                command.CommandTimeout = config.Database.DbTimeout;

                using (var reader = await command.ExecuteReaderAsync())
                {
                    int featureCount = 0;
                    while (await reader.ReadAsync())
                    {
                        Geometry? geometry = null;
                        var attributes = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            // Check if the column is of type SqlGeometry or SqlGeography
                            if (reader.GetFieldType(i) == typeof(SqlGeometry) || reader.GetFieldType(i) == typeof(SqlGeography))
                            {
                                var geometryReader = new SqlServerBytesReader
                                {
                                    IsGeography = reader.GetFieldType(i) == typeof(SqlGeography)
                                };
                                var bytes = reader.GetSqlBytes(i).Value;
                                geometry = geometryReader.Read(bytes);
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
                            featureCount++;
                        }
                    }
                    
                    _logger.LogDebug("Retrieved {FeatureCount} features for layer {LayerName}", featureCount, layername);
                }
            }
        }

        return tileData;
    }

    private List<IFeature> TileDataToFeatures(TileData tileData)
    {
        List<IFeature> features = new List<IFeature>(tileData.Geometries.Count);

        for (int i = 0; i < tileData.Geometries.Count; i++)
        {
            Geometry geometry = tileData.Geometries[i];
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

    public async Task<VectorTile> GetVectorTile(Config config, LayerMeta layerMeta, int z, int x, int y)
    {
        _logger.LogDebug("Generating vector tile for layer {LayerName} at z={Z}, x={X}, y={Y}", layerMeta.Name, z, x, y);
        
        Envelope bounds = TileHelper.TileIdToBounds(x, y, z);
        Envelope bufferedBounds = TileHelper.TileIdToBounds(x, y, z, config.Tile.Extent, config.Tile.Buffer);

        Geometry boundsGeometry = new GeometryFactory(new PrecisionModel(), EPSG_4326).ToGeometry(bounds);
        Geometry bufferedBoundsGeometry = new GeometryFactory(new PrecisionModel(), EPSG_4326).ToGeometry(bufferedBounds);

        if (layerMeta.SRID != EPSG_4326)
        {
            _logger.LogTrace("Projecting bounds from EPSG:4326 to SRID {SRID} for layer {LayerName}", layerMeta.SRID, layerMeta.Name);
            // Project the bounds to the layer's SRID
            boundsGeometry = boundsGeometry.ProjectTo(layerMeta.SRID);
            bufferedBoundsGeometry = bufferedBoundsGeometry.ProjectTo(layerMeta.SRID);
        }

        var tileDefinition = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x, y, z);
        VectorTile vectorTile = new VectorTile { TileId = tileDefinition.Id };

        string layername = layerMeta.Name;
        TileData tileData = await GetTileData(config, boundsGeometry, bufferedBoundsGeometry, layerMeta);

        // If the projection of the layer is not WGS84, we need to transform the geometries in the tileData
        if (layerMeta.SRID != EPSG_4326)
        {
            _logger.LogTrace("Transforming geometries from SRID {SRID} to EPSG:4326 for layer {LayerName}", layerMeta.SRID, layerMeta.Name);
            for (int i = 0; i < tileData.Geometries.Count; i++)
            {
                tileData.Geometries[i].SRID = layerMeta.SRID;
                tileData.Geometries[i] = tileData.Geometries[i].ProjectTo(EPSG_4326);
            }
        }

        Layer layer = CreateVectorTileLayer(layername, tileData);
        vectorTile.Layers.Add(layer);

        _logger.LogDebug("Successfully generated vector tile for layer {LayerName} with {FeatureCount} features", layerMeta.Name, tileData.Geometries.Count);
        return vectorTile;
    }

    // TODO: Support multiple layers in a single tile
    public async Task<byte[]> GetVectorTileBytes(Config config, LayerMeta layerMeta, int z, int x, int y)
    {
        _logger.LogDebug("Generating vector tile bytes for layer {LayerName} at z={Z}, x={X}, y={Y}", layerMeta.Name, z, x, y);
        
        VectorTile vt = await GetVectorTile(config, layerMeta, z, x, y);
        byte[] tile;
        using (var ms = new MemoryStream())
        {
            vt.Write(ms, MapboxTileWriter.DefaultMinLinealExtent, MapboxTileWriter.DefaultMinPolygonalExtent);
            tile = ms.ToArray();
        }

        int uncompressedSize = tile.Length;
        tile = CompressMVT(tile);
        int compressedSize = tile.Length;
        
        _logger.LogTrace("Compressed tile from {UncompressedSize} to {CompressedSize} bytes (ratio: {CompressionRatio:P1})", 
            uncompressedSize, compressedSize, (double)compressedSize / uncompressedSize);

        return tile;
    }

    public static List<LayerMeta> GetAvailableTables(string connectionString, Config config)
    {
        Log.Information("Checking available layers in database");
        var layers = new List<LayerMeta>();
        var layerDict = new Dictionary<string, LayerMeta>();

        const string sqlFindTableInfo = @"
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
            o.type IN ('U', 'V')
            AND ty.name IN ('geometry', 'geography');";

        const string sqlFindSpatialIndex = @"
        SELECT
            t.name AS table_name
        FROM
            sys.tables t
        JOIN
            sys.schemas s ON t.schema_id = s.schema_id
        JOIN
            sys.indexes i ON i.object_id = t.object_id AND i.type_desc = 'SPATIAL';";

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        // 1. Get geometry tables/views
        using (var command = connection.CreateCommand())
        {
            Log.Information("Scanning database for geometry/geography columns");
            command.CommandText = sqlFindTableInfo;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var objectName = reader.GetString(reader.GetOrdinal("ObjectName"));
                var objectType = reader.GetString(reader.GetOrdinal("ObjectType"));
                var columnName = reader.GetString(reader.GetOrdinal("ColumnName"));
                var typeName = reader.GetString(reader.GetOrdinal("TypeName"));

                if (!layerDict.ContainsKey(objectName))
                {
                    var layer = new LayerMeta
                    {
                        Name = objectName,
                        ObjectType = objectType,
                        GeometryColumnName = columnName,
                        GeometryTypeName = typeName
                    };
                    layers.Add(layer);
                    layerDict[objectName] = layer;
                    Log.Debug("Found {ObjectType} '{ObjectName}' with {GeometryType} column '{ColumnName}'", 
                        objectType == "U" ? "table" : "view", objectName, typeName, columnName);
                }
            }
        }

        // 2. Get SRID for each layer
        Log.Information("Checking SRIDs for {LayerCount} geometry/geography columns", layers.Count);
        foreach (var layer in layers)
        {
            var sqlFindSrid = $@"
            SELECT
                [{layer.GeometryColumnName}].STSrid AS SRID
            FROM
                [{layer.Name}]
            WHERE
                [{layer.GeometryColumnName}] IS NOT NULL
            GROUP BY
                [{layer.GeometryColumnName}].STSrid";

            using var command = connection.CreateCommand();
            command.CommandText = sqlFindSrid;
            using var sridReader = command.ExecuteReader();
            int count = 0;
            while (sridReader.Read())
            {
                if (count > 0)
                {
                    layer.HealthLevel = LayerHealthLevel.Unhealthy;
                    layer.HealthMessages.Add($"Layer '{layer.Name}' has multiple SRIDs. It will not be used to serve tiles.");
                    Log.Warning("Layer {LayerName} has multiple SRIDs and will be marked as unhealthy", layer.Name);
                    break;
                }
                layer.SRID = sridReader.GetInt32(sridReader.GetOrdinal("SRID"));
                Log.Debug("Layer {LayerName} uses SRID {SRID}", layer.Name, layer.SRID);
                count++;
            }
        }

        // 3. Get spatial index info
        var hasSpatialIndexLayers = new HashSet<string>();
        using (var command = connection.CreateCommand())
        {
            Log.Information("Checking for spatial indexes in database");
            command.CommandText = sqlFindSpatialIndex;
            using var spatialIndexReader = command.ExecuteReader();
            while (spatialIndexReader.Read())
            {
                var tableName = spatialIndexReader.GetString(spatialIndexReader.GetOrdinal("table_name"));
                hasSpatialIndexLayers.Add(tableName);
                Log.Debug("Found spatial index on table {TableName}", tableName);
            }
        }

        // 4. Set spatial index and health info
        foreach (var layer in layers)
        {
            layer.HasSpatialIndex = hasSpatialIndexLayers.Contains(layer.Name);
            if (!layer.HasSpatialIndex && layer.ObjectType == "U") // Views cannot have a spatial index in SQL Server.
            {
                if (layer.HealthLevel == LayerHealthLevel.Unhealthy)
                {
                    layer.HealthMessages.Add($"Layer '{layer.Name}' does not have a spatial index.");
                }
                else
                {
                    layer.HealthLevel = LayerHealthLevel.Warning;
                    layer.HealthMessages.Add($"Layer '{layer.Name}' does not have a spatial index. This may affect performance.");
                    Log.Warning("Table {LayerName} does not have a spatial index, which may affect performance", layer.Name);
                }
            }
        }

        foreach (var layer in layers)
        {
            Log.Debug("Getting column information for layer {LayerName}", layer.Name);
            List<string> columns = TileService.GetTableColumns(connectionString, config.Database.Schema, layer.Name);
            layer.Columns = columns;
            Log.Debug("Layer {LayerName} has {ColumnCount} columns", layer.Name, columns.Count);
        }

        Log.Information("Layer discovery completed. Found {TotalLayers} layers: {HealthyLayers} healthy, {WarningLayers} with warnings, {UnhealthyLayers} unhealthy",
            layers.Count,
            layers.Count(l => l.HealthLevel == LayerHealthLevel.Healthy),
            layers.Count(l => l.HealthLevel == LayerHealthLevel.Warning),
            layers.Count(l => l.HealthLevel == LayerHealthLevel.Unhealthy));

        return layers;
    }

    public static List<string> GetTableColumns(string connectionString, string schema, string tableName)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.IsNullOrWhiteSpace(schema) ||
            string.IsNullOrWhiteSpace(tableName))
        {
            return new List<string>();
        }

        var columns = new List<string>();
        const string sql = @"
          SELECT COLUMN_NAME
          FROM INFORMATION_SCHEMA.COLUMNS
          WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
        ";

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = tableName });

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
