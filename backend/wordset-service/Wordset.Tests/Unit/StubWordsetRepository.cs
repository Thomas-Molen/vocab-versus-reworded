using Wordset.Domain.Interfaces;

namespace Wordset.Tests.Unit;

/// <summary>
/// Minimal in-memory stub of IWordsetRepository for unit testing WordsetService logic.
/// </summary>
internal class StubWordsetRepository(
    Domain.Entities.Wordset? existing = null,
    bool[]? shareCodeExistsSequence = null) : IWordsetRepository
{
    private readonly List<Domain.Entities.Wordset> _store = existing is not null ? [existing] : [];
    private readonly Queue<bool> _shareCodeExists = new(shareCodeExistsSequence ?? [false]);
    private readonly Dictionary<string, int?> _words = [];

    public int ShareCodeExistsCallCount { get; private set; }
    public int GetByShareCodeCallCount { get; private set; }

    public Task<Domain.Entities.Wordset?> GetByShareCodeAsync(string shareCode, CancellationToken ct = default)
    {
        GetByShareCodeCallCount++;
        return Task.FromResult(_store.FirstOrDefault(w => w.ShareCode == shareCode));
    }

    public Task<Domain.Entities.Wordset?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(w => w.Id == id));

    public Task<IReadOnlyList<Domain.Entities.Wordset>> GetWordsetPageAsync(string? afterShareCode, int pageSize, CancellationToken ct = default)
    {
        var page = _store
            .OrderBy(w => w.ShareCode, StringComparer.Ordinal)
            .Where(w => afterShareCode == null || string.Compare(w.ShareCode, afterShareCode, StringComparison.Ordinal) > 0)
            .Take(pageSize)
            .ToList();
        return Task.FromResult<IReadOnlyList<Domain.Entities.Wordset>>(page);
    }

    public Task<Domain.Entities.Wordset> CreateAsync(Domain.Entities.Wordset wordset, CancellationToken ct = default)
    {
        _store.Add(wordset);
        return Task.FromResult(wordset);
    }

    public Task<Domain.Entities.Wordset> UpdateAsync(Domain.Entities.Wordset wordset, CancellationToken ct = default)
        => Task.FromResult(wordset);

    public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken ct = default)
    {
        ShareCodeExistsCallCount++;
        return Task.FromResult(_shareCodeExists.Count > 0 && _shareCodeExists.Dequeue());
    }

    public Task<IReadOnlyList<string>> GetWordsPageAsync(Guid wordsetId, string? afterWord, int pageSize, CancellationToken ct = default)
    {
        var page = _words.Keys
            .OrderBy(w => w, StringComparer.Ordinal)
            .Where(w => afterWord == null || string.Compare(w, afterWord, StringComparison.Ordinal) > 0)
            .Take(pageSize)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(page);
    }

    public Task<int> GetWordCountAsync(Guid wordsetId, CancellationToken ct = default)
        => Task.FromResult(_words.Count);

    public Task<IReadOnlyDictionary<string, int?>> GetWordFrequencyMapAsync(Guid wordsetId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, int?>>(new Dictionary<string, int?>(_words));

    public Task ReplaceWordsAsync(Guid wordsetId, IReadOnlyList<Domain.Entities.Word> words, CancellationToken ct = default)
    {
        _words.Clear();
        foreach (var word in words)
            _words[word.Value] = word.FrequencyRank;
        return Task.CompletedTask;
    }
}
