using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MssqlTileServ.Cli.Endpoints
{
    public static class HomeEndpoint
    {
        public static void MapHomeEndpoint(this WebApplication app)
        {
            app.MapGet("/", (HttpContext context, ILogger<Program> logger) =>
            {
                logger.LogInformation("Home page accessed from {RemoteIP}", context.Connection.RemoteIpAddress);
                return Results.Content("<h1>Welcome to the mssql_tileserv API!</h1><p>Visit <a href=\"/monitor\">monitor</a> to see the monitoring page.</p>", "text/html");
            });
        }
    }
}
