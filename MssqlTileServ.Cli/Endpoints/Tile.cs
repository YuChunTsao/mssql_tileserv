using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Services;

namespace MssqlTileServ.Cli.Endpoints
{
    public static class TileEndpoint
    {
        public static void MapTileEndpoint(this WebApplication app, List<LayerMeta> layers, string connectionString, Config config)
        {
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
        }
    }
}
