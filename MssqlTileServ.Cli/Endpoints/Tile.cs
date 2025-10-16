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
        public static void MapTileEndpoint(this WebApplication app, List<LayerMeta> layerMetas, string connectionString, Config config)
        {
            var tileCache = new TileCache();
            app.MapGet("{layers}/{z:int}/{x:int}/{y:int}", async (HttpContext context, string layers, int z, int x, int y, TileService tileService, ILogger<Program> logger) =>
            {
                var stopwatch = Stopwatch.StartNew();
                logger.LogInformation("Tile request: {Layers} {Z}/{X}/{Y} from {RemoteIP}", layers, z, x, y, context.Connection.RemoteIpAddress);

                string cacheKey = $"{layers}-{z}/{x}/{y}";
                var requestLayers = layers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                List<LayerMeta> selectedLayers = layerMetas
                    .Where(l => requestLayers.Contains(l.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var missingLayers = requestLayers
                    .Where(rl => !layerMetas.Any(l => l.Name.Equals(rl, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (missingLayers.Count > 0)
                {
                    logger.LogWarning("Requested layers not found: {MissingLayers}", string.Join(", ", missingLayers));
                }

                if (selectedLayers.Count == 0)
                {
                    logger.LogWarning("No valid layers found in request '{Layers}' for tile {Z}/{X}/{Y}", layers, z, x, y);
                    context.Response.StatusCode = 404;
                    return Results.NotFound($"No valid layers found in request '{layers}'.");
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
                            return await tileService.GetVectorTileBytes(config, selectedLayers, z, x, y);
                        }, TimeSpan.FromSeconds(config.Service.MemoryExpirationSeconds));
                    }
                    else
                    {
                        tile = await tileService.GetVectorTileBytes(config, selectedLayers, z, x, y);
                    }

                    stopwatch.Stop();
                    logger.LogInformation("Tile served: {Layers} {Z}/{X}/{Y} in {Duration}ms (size: {Size} bytes, cached: {FromCache})",
                        layers, z, x, y, stopwatch.ElapsedMilliseconds, tile.Length, fromCache);

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
                    logger.LogError(ex, "Error generating tile for {Layers} {Z}/{X}/{Y} after {Duration}ms",
                        layers, z, x, y, stopwatch.ElapsedMilliseconds);
                    context.Response.StatusCode = 500;
                    return Results.Problem("An error occurred while generating the tile");
                }
            });
        }
    }
}
