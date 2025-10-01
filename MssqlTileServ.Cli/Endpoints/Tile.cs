using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Services;
using System.Diagnostics;

namespace MssqlTileServ.Cli.Endpoints
{
    public static class TileEndpoint
    {
        public static void MapTileEndpoint(this WebApplication app, List<LayerMeta> layers, string connectionString, Config config)
        {
            var tileCache = new TileCache();
            app.MapGet("{layer}/{z:int}/{x:int}/{y:int}", async (HttpContext context, string layer, int z, int x, int y, TileService tileService, ILogger<Program> logger) =>
            {
                var stopwatch = Stopwatch.StartNew();
                logger.LogInformation("Tile request: {Layer} {Z}/{X}/{Y} from {RemoteIP}", layer, z, x, y, context.Connection.RemoteIpAddress);

                string cacheKey = $"{layer}-{z}/{x}/{y}";

                LayerMeta? layerMeta = layers.FirstOrDefault(l => l.Name.Equals(layer));
                if (layerMeta == null)
                {
                    logger.LogWarning("Layer '{Layer}' not found for tile request {Z}/{X}/{Y}", layer, z, x, y);
                    context.Response.StatusCode = 404;
                    return Results.NotFound($"Layer '{layer}' not found.");
                }

                try
                {
                    byte[] tile;
                    bool memoryCacheEnabled = config.Service.MemoryExpirationSeconds > 0;
                    bool fromCache = false;

                    if (memoryCacheEnabled)
                    {
                        (tile, fromCache) = await tileCache.GetOrAddWithCacheInfoAsync(cacheKey, async () =>
                        {
                            logger.LogDebug("Cache miss for tile {CacheKey}, generating new tile", cacheKey);
                            return await tileService.GetVectorTileBytes(config, layerMeta, z, x, y);
                        }, TimeSpan.FromSeconds(config.Service.MemoryExpirationSeconds));
                    }
                    else
                    {
                        tile = await tileService.GetVectorTileBytes(config, layerMeta, z, x, y);
                    }

                    stopwatch.Stop();
                    logger.LogInformation("Tile served: {Layer} {Z}/{X}/{Y} in {Duration}ms (size: {Size} bytes, cached: {FromCache})",
                        layer, z, x, y, stopwatch.ElapsedMilliseconds, tile.Length, fromCache);

                    if (config.Service.CacheTTL > 0)
                    {
                        context.Response.Headers["Cache-Control"] = $"public, max-age={config.Service.CacheTTL}";
                    }
                    context.Response.Headers["Content-Encoding"] = "gzip";

                    return Results.File(tile, "application/x-protobuf", $"tile_{z}_{x}_{y}.mvt");
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    logger.LogError(ex, "Error generating tile for {Layer} {Z}/{X}/{Y} after {Duration}ms",
                        layer, z, x, y, stopwatch.ElapsedMilliseconds);
                    context.Response.StatusCode = 500;
                    return Results.Problem("An error occurred while generating the tile");
                }
            });
        }
    }
}
