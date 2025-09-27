using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MssqlTileServ.Cli.Services;
using MssqlTileServ.Cli.Utils;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Endpoints;

namespace MssqlTileServ.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            RootCommand rootCommand = new("mssql_tileserv CLI tool");

            rootCommand.SetAction((parseResult) =>
            {
                string configFilePath = Path.Combine(AppContext.BaseDirectory, "config", "config.toml");
                Config config = TomlConfigLoader.Load(configFilePath);
                string connectionString = DbUtils.GetConnectionString(config);

                SridWktLoader.LoadFromCsv("Resources/epsg_wkt_mapping.csv");

                var builder = WebApplication.CreateBuilder();

                ServiceConfigUtils.SetCORSOrigins(builder, config);

                builder.Services.AddSingleton<TileService>(sp =>
                {
                    return new TileService(connectionString);
                });

                var app = builder.Build();

                // Use CORS middleware
                app.UseCors();

                List<LayerMeta> layers = TileService.GetAvailableTables(connectionString, config);

                app.MapHomeEndpoint();
                app.MapMonitorEndpoint();
                app.MapTileEndpoint(layers, connectionString, config);
                app.MapLayerEndpoint(layers);

                app.Run();
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
    }
}
