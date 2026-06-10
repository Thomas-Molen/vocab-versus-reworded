using Microsoft.Extensions.Caching.Memory;
using Wordset.Domain.Interfaces;

namespace Wordset.Infrastructure;

public class ShareCodeCache(IMemoryCache cache) : IShareCodeCache
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
    };

    private static string Key(string shareCode) => $"sc:{shareCode}";

    public Guid? TryGet(string shareCode)
        => cache.TryGetValue(Key(shareCode), out Guid id) ? id : null;

    public void Set(string shareCode, Guid id)
        => cache.Set(Key(shareCode), id, CacheOptions);
}
