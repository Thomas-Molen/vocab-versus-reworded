namespace Wordset.Tests.Unit;

/// <summary>Helper stub that captures the normalised word passed to ExactMatchAsync.</summary>
internal class CapturingWordGameRepository(Func<string, string?> onExactMatch) : Domain.Interfaces.IWordGameRepository
{
    public Task<IReadOnlyList<string>> GetRandomWordsAsync(Guid wordsetId, int count, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> ExactMatchAsync(Guid wordsetId, string normalizedWord, CancellationToken ct = default)
        => Task.FromResult(onExactMatch(normalizedWord));

    public Task<string?> FuzzyMatchAsync(Guid wordsetId, string normalizedWord, int maxDistance, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
