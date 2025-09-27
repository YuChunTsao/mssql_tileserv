using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace MssqlTileServ.Cli.Endpoints
{
    public static class MonitorEndpoint
    {
        public static void MapMonitorEndpoint(this WebApplication app)
        {
            var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "MssqlTileServ.Cli.wwwroot");
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
        }
    }
}
