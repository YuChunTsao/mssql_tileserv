using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MssqlTileServ.Cli.Services;
using MssqlTileServ.Cli.Utils;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Endpoints;
using Serilog;
using Serilog.Events;

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

                // Parse the log level from config
                if (!Enum.TryParse<LogEventLevel>(config.Logging.LogLevel, true, out LogEventLevel logLevel))
                {
                    logLevel = LogEventLevel.Information;
                }

                // Configure Serilog based on configuration
                var loggerConfig = new LoggerConfiguration()
                    .MinimumLevel.Is(logLevel)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .Enrich.FromLogContext();

                if (config.Logging.LogToConsole)
                {
                    loggerConfig.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                }

                if (config.Logging.LogToFile)
                {
                    // Ensure log file path is absolute to avoid duplication
                    string logFilePath = config.Logging.LogFilePath;
                    if (!Path.IsPathRooted(logFilePath))
                    {
                        // Make path relative to the solution root, not the executable directory
                        string solutionRoot = Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory)) ?? AppContext.BaseDirectory;
                        logFilePath = Path.Combine(solutionRoot, logFilePath);
                    }

                    loggerConfig.WriteTo.File(logFilePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: config.Logging.MaxLogFiles,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
                }

                Log.Logger = loggerConfig.CreateLogger();

                try
                {
                    Log.Information("Starting MSSQL Tile Server with log level {LogLevel}", logLevel);

                    Log.Information("Loading configuration from {ConfigPath}", configFilePath);
                    string connectionString = DbUtils.GetConnectionString(config);

                    SridWktProvider.Init();

                    var builder = WebApplication.CreateBuilder();

                    // Add Serilog to the logging pipeline
                    builder.Host.UseSerilog();

                    ServiceConfigUtils.SetCORSOrigins(builder, config);

                    builder.Services.AddSingleton<TileService>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<TileService>>();
                        return new TileService(connectionString, logger);
                    });

                    var app = builder.Build();

                    // Use CORS middleware
                    app.UseCors();

                    Log.Information("Retrieving available layers from database");
                    List<LayerMeta> layers = TileService.GetAvailableTables(connectionString, config);
                    Log.Information("Found {LayerCount} available layers", layers.Count);

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

                    Log.Information("MSSQL Tile Server started successfully");
                    app.Run();
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Application start-up failed");
                    throw;
                }
                finally
                {
                    Log.CloseAndFlush();
                }
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
    }
}
