using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace MssqlTileServ.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            RootCommand rootCommand = new("mssql_tileserv CLI tool");

            rootCommand.SetAction((parseResult) =>
            {
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
