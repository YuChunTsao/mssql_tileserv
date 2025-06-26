using Microsoft.Extensions.Caching.Memory;

public class TileCache
{
    private readonly MemoryCache _cache;

    public TileCache()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<byte[]> GetOrAddAsync(string key, Func<Task<byte[]>> valueFactory, TimeSpan expiration)
    {
        if (!_cache.TryGetValue(key, out byte[] value))
        {
            value = await valueFactory();
            _cache.Set(key, value, expiration);
        }
        return value;
    }
}
