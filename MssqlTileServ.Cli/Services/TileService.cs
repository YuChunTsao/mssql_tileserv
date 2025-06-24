using System.Data;
using Microsoft.SqlServer.Types;
using MssqlTileServ.Cli.Models;
using NetTopologySuite.Geometries;

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
        if (config.Database == null)
        {
            throw new ArgumentException("Database configuration is not provided.");
        }

        if (config.Database?.Name == null)
        {
            throw new ArgumentException("Database name is not configured.");
        }

        if (config.Database?.User == null || config.Database?.Password == null)
        {
            throw new ArgumentException("Database user or password is not configured.");
        }

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

    public Task<TileData> GetTile(Config config, Envelope bounds, string layername)
    {
        if (config.Database == null)
        {
            throw new ArgumentException("Database configuration is not provided.");
        }

        if (string.IsNullOrEmpty(config.Database.Name))
        {
            throw new ArgumentException("Database name is not configured.");
        }

        if (string.IsNullOrEmpty(config.Database.User) || string.IsNullOrEmpty(config.Database.Password))
        {
            throw new ArgumentException("Database user or password is not configured.");
        }

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
}
