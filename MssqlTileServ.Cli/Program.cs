using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using MssqlTileServ.Cli.Services;
using MssqlTileServ.Cli.Utils;
using MssqlTileServ.Cli.Models;
using Microsoft.Extensions.FileProviders;
using System.Reflection;

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
                    $"Max Pool Size={config.Database.DbPoolMaxConns}";

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

                builder.Services.AddTransient<TileService>(sp =>
                {
                    return new TileService(connectionString);
                });

                var app = builder.Build();

                // Use CORS middleware
                app.UseCors();

                // Check all available layers
                Console.WriteLine("Checking available layers...");
                List<LayerMeta> layers = TileService.GetAvailableTables(connectionString);
                foreach (var layer in layers)
                {
                    List<string> columns = TileService.GetTableColumns(connectionString, config.Database.Schema, layer.Name);
                    layer.Columns = columns;
                }
                Console.WriteLine("Finished checking layers.");

                SridWktLoader.LoadFromCsv("Resources/epsg_wkt_mapping.csv");

                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "MssqlTileServ.Cli.wwwroot");

                app.MapGet("/", () =>
                {
                    return "Welcome to the mssql_tileserv API!";
                });

                app.MapGet("/layers", () =>
                {
                    return Results.Json(layers);
                });

                app.MapGet("/monitor", async context =>
                {
                    var fileInfo = embeddedProvider.GetFileInfo("monitor.html");
                    if (!fileInfo.Exists)
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("Monitor page not found.");
                        return;
                    }
                    context.Response.ContentType = "text/html";
                    using var stream = fileInfo.CreateReadStream();
                    await stream.CopyToAsync(context.Response.Body);
                });

                var tileCache = new TileCache();
                app.MapGet("{layer}/{z:int}/{x:int}/{y:int}", async (HttpContext context, string layer, int z, int x, int y, TileService tileService) =>
                {
                    string cacheKey = $"{layer}-{z}/{x}/{y}";

                    LayerMeta? layerMeta = layers.FirstOrDefault(l => l.Name.Equals(layer));
                    if (layerMeta == null)
                    {
                        context.Response.StatusCode = 404;
                        return Results.NotFound($"Layer '{layer}' not found.");
                    }

                    byte[] tile;
                    bool memoryCacheEnabled = config.Service.MemoryExpirationSeconds > 0;
                    if (memoryCacheEnabled)
                    {
                        tile = await tileCache.GetOrAddAsync(cacheKey, async () =>
                        {
                            return await tileService.GetVectorTileBytes(config, layerMeta, z, x, y);
                        }, TimeSpan.FromSeconds(config.Service.MemoryExpirationSeconds));
                    }
                    else
                    {
                        tile = await tileService.GetVectorTileBytes(config, layerMeta, z, x, y);
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
