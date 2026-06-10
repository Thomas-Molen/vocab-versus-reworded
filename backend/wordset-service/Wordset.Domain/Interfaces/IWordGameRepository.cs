namespace Wordset.Domain.Interfaces;

public interface IWordGameRepository
{
    /// <summary>
    /// Returns up to <paramref name="count"/> randomly-ordered words from the wordset partition.
    /// The caller filters for a qualifying word (enough distinct non-excluded characters).
    /// </summary>
    Task<IReadOnlyList<string>> GetRandomWordsAsync(Guid wordsetId, int count, CancellationToken ct = default);

    /// <summary>
    /// Returns the stored word if an exact match exists (after excluded-char normalisation), otherwise null.
    /// </summary>
    Task<string?> ExactMatchAsync(Guid wordsetId, string normalizedWord, CancellationToken ct = default);

    /// <summary>
    /// Returns the closest matching word within <paramref name="maxDistance"/> Levenshtein distance,
    /// using trigram similarity as a pre-filter. Returns null if no match is found.
    /// </summary>
    Task<string?> FuzzyMatchAsync(Guid wordsetId, string normalizedWord, int maxDistance, CancellationToken ct = default);
}
