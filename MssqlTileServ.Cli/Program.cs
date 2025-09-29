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

                SridWktProvider.Init();

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

                Task.Run(() =>
                {
                    while (true)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.H)
                        {
                            Console.WriteLine("Available commands:");
                            Console.WriteLine("H - Show this help message");
                        }
                        // Add more key actions here if needed
                    }
                });

                app.Run();
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
    }
}
