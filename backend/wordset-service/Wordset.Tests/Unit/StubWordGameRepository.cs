using Wordset.Domain.Interfaces;

namespace Wordset.Tests.Unit;

internal class StubWordGameRepository : IWordGameRepository
{
    private readonly IReadOnlyList<string> _words;
    private readonly string? _exactMatch;
    private readonly string? _fuzzyMatch;

    public StubWordGameRepository(
        IEnumerable<string>? words = null,
        string? exactMatch = null,
        string? fuzzyMatch = null)
    {
        _words = words?.ToList() ?? [];
        _exactMatch = exactMatch;
        _fuzzyMatch = fuzzyMatch;
    }

    public Task<IReadOnlyList<string>> GetRandomWordsAsync(Guid wordsetId, int count, CancellationToken ct = default)
    {
        var result = _words.Take(count).ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<string?> ExactMatchAsync(Guid wordsetId, string normalizedWord, CancellationToken ct = default)
        => Task.FromResult(_exactMatch);

    public Task<string?> FuzzyMatchAsync(Guid wordsetId, string normalizedWord, int maxDistance, CancellationToken ct = default)
        => Task.FromResult(_fuzzyMatch);
}
