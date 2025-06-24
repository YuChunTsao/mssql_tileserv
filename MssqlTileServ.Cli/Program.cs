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
                    $"Server={config.Database?.Server},{config.Database?.Port};" +
                    $"Database={config.Database?.Name};" +
                    $"User Id={config.Database?.User};" +
                    $"Password={config.Database?.Password};" +
                    $"TrustServerCertificate=True;" +
                    $"Pooling=true;" +
                    $"Max Pool Size={config.Database?.DbPoolMaxConns}";

                var builder = WebApplication.CreateBuilder();
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyHeader()
                              .AllowAnyMethod();
                    });
                });
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

                app.MapGet("{layer}/{z:int}/{x:int}/{y:int}", (HttpContext context, string layer, int z, int x, int y) =>
                {
                    context.Response.ContentType = "text/plain";
                    return $"Layer: {layer}, Z: {z}, X: {x}, Y: {y}";
                });

                app.Run();
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
    }
}
