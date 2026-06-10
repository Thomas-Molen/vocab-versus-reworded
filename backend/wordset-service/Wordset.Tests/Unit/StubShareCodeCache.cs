using Wordset.Domain.Interfaces;

namespace Wordset.Tests.Unit;

internal class StubShareCodeCache : IShareCodeCache
{
    private readonly Dictionary<string, Guid> _cache = [];

    public Guid? TryGet(string shareCode)
        => _cache.TryGetValue(shareCode, out var id) ? id : null;

    public void Set(string shareCode, Guid id)
        => _cache[shareCode] = id;

    public bool Contains(string shareCode) => _cache.ContainsKey(shareCode);
}
