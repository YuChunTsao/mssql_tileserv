using Microsoft.Extensions.Caching.Memory;

public class TileCache
{
    private readonly MemoryCache _cache;

    public TileCache()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<(byte[] data, bool fromCache)> GetOrAddWithCacheInfoAsync(string key, Func<Task<byte[]>> valueFactory, TimeSpan expiration)
    {
        if (_cache.TryGetValue(key, out byte[]? value) && value is not null)
        {
            return (value, true); // Cache hit
        }

        // Cache miss - generate new value
        value = await valueFactory();
        _cache.Set(key, value, expiration);
        return (value, false); // Not from cache
    }
}
