using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MssqlTileServ.Cli.Models;

namespace MssqlTileServ.Cli.Endpoints
{
    public static class LayerEndpoint
    {
        public static void MapLayerEndpoint(this WebApplication app, List<LayerMeta> layers)
        {
            app.MapGet("/layers", () =>
            {
                return Results.Json(layers);
            });
        }
    }
}
