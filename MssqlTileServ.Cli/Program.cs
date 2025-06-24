using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using MssqlTileServ.Cli.Services;
using MssqlTileServ.Cli.Utils;
using MssqlTileServ.Cli.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace MssqlTileServ.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            RootCommand rootCommand = new("mssql_tileserv CLI tool");

            string configFilePath = Path.Combine(AppContext.BaseDirectory, "config", "config.toml");

            rootCommand.SetAction((parseResult) =>
            {
                Config config = TomlConfigLoader.Load(configFilePath);
                string connectionString =
                    $"Server={config.Database.Server},{config.Database.Port};" +
                    $"Database={config.Database.Name};" +
                    $"User Id={config.Database.User};" +
                    $"Password={config.Database.Password};" +
                    $"TrustServerCertificate=True;" +
                    $"Pooling=true;" +
                    $"Max Pool Size={config.Database?.DbPoolMaxConns}";

                var builder = WebApplication.CreateBuilder();

                // Control the Cors policy based on the config
                if (config.Service.CORSOrigins.Length > 0 && config.Service.CORSOrigins[0] != "*")
                {
                    builder.Services.AddCors(options =>
                    {
                        options.AddDefaultPolicy(policy =>
                        {
                            policy.WithOrigins(config.Service.CORSOrigins)
                                  .AllowAnyHeader()
                                  .AllowAnyMethod();
                        });
                    });
                }
                else
                {
                    // Allow any origin if no specific origins are configured
                    builder.Services.AddCors(options =>
                    {
                        options.AddDefaultPolicy(policy =>
                        {
                            policy.AllowAnyOrigin()
                                  .AllowAnyHeader()
                                  .AllowAnyMethod();
                        });
                    });
                }

                builder.Services.AddScoped<IDbConnection>(_ =>
                    new SqlConnection(connectionString));
                builder.Services.AddScoped<TileService>();

                var app = builder.Build();

                // Use CORS middleware
                app.UseCors();

                app.MapGet("/", () =>
                {
                    return "Welcome to the mssql_tileserv API!";
                });

                var tileCache = new TileCache();
                app.MapGet("{layer}/{z:int}/{x:int}/{y:int}", (HttpContext context, string layer, int z, int x, int y) =>
                {
                    string cacheKey = $"{layer}-{z}/{x}/{y}";

                    byte[] tile;
                    if (config.Service.MemoryExpirationSeconds > 0)
                    {
                        tile = tileCache.GetOrAdd(cacheKey, () =>
                        {
                            TileService tileService = new TileService(context.RequestServices.GetRequiredService<IDbConnection>());
                            return tileService.GetVectorTileBytes(config, layer, z, x, y);
                        }, TimeSpan.FromSeconds(config.Service.MemoryExpirationSeconds));
                    }
                    else
                    {
                        TileService tileService = new TileService(context.RequestServices.GetRequiredService<IDbConnection>());
                        tile = tileService.GetVectorTileBytes(config, layer, z, x, y);
                    }

                    if (config.Service.CacheTTL > 0)
                    {
                        context.Response.Headers["Cache-Control"] = $"public, max-age={config.Service.CacheTTL}";
                    }
                    context.Response.Headers["Content-Encoding"] = "gzip";

                    return Results.File(tile, "application/x-protobuf", $"tile_{z}_{x}_{y}.mvt");
                });

                app.Run();
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
    }
}
