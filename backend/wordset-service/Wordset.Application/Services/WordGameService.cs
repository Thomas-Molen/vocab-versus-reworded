using Wordset.Application.Models;
using Wordset.Domain.Exceptions;
using Wordset.Domain.Interfaces;

namespace Wordset.Application.Services;

public class WordGameService(
    IWordsetRepository wordsetRepository,
    IWordGameRepository wordGameRepository,
    IShareCodeCache shareCodeCache)
{
    // Characters that are never returned as challenge letters and are stripped before word comparison.
    public static readonly IReadOnlySet<char> ExcludedChars = new HashSet<char> { ' ', '-' };

    // How many random word candidates to fetch when generating a challenge, before filtering for eligible chars.
    private const int RandomWordCandidateCount = 10;

    public async Task<ChallengeDto> GetChallengeAsync(string shareCode, int letterCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
            throw new ArgumentException("Share code is required.", nameof(shareCode));
        if (letterCount < 1)
            throw new ArgumentOutOfRangeException(nameof(letterCount), "Letter count must be at least 1.");

        var wordsetId = await ResolveWordsetIdAsync(shareCode, ct);

        var candidates = await wordGameRepository.GetRandomWordsAsync(wordsetId, RandomWordCandidateCount, ct);

        var qualifying = candidates
            .Select(w => DistinctEligibleChars(w))
            .FirstOrDefault(chars => chars.Count >= letterCount);

        if (qualifying is null)
            throw new InvalidOperationException(
                $"No word in wordset '{shareCode}' has at least {letterCount} distinct non-excluded characters.");

        var letters = SampleAndShuffle(qualifying, letterCount);
        return new ChallengeDto(letters);
    }

    public async Task<ValidationResultDto> ValidateWordAsync(string shareCode, string word, int fuzzyTolerance, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareCode))
            throw new ArgumentException("Share code is required.", nameof(shareCode));
        if (string.IsNullOrEmpty(word))
            throw new ArgumentException("Word is required.", nameof(word));
        if (fuzzyTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(fuzzyTolerance), "Fuzzy tolerance must be 0 or greater.");

        var wordsetId = await ResolveWordsetIdAsync(shareCode, ct);

        var normalized = StripExcludedChars(word.ToLowerInvariant());

        string? matched = fuzzyTolerance == 0
            ? await wordGameRepository.ExactMatchAsync(wordsetId, normalized, ct)
            : await wordGameRepository.FuzzyMatchAsync(wordsetId, normalized, fuzzyTolerance, ct);

        return matched is not null
            ? new ValidationResultDto(Valid: true, MatchedWord: matched)
            : new ValidationResultDto(Valid: false, MatchedWord: string.Empty);
    }

    internal static string StripExcludedChars(string word)
        => new string(word.Where(c => !ExcludedChars.Contains(c)).ToArray());

    private static List<char> DistinctEligibleChars(string word)
        => word.Where(c => !ExcludedChars.Contains(c)).Distinct().ToList();

    private static IReadOnlyList<string> SampleAndShuffle(List<char> eligible, int count)
    {
        // Fisher-Yates partial shuffle to sample without replacement
        var pool = new List<char>(eligible);
        var sampled = new char[count];
        for (int i = 0; i < count; i++)
        {
            var j = Random.Shared.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            sampled[i] = pool[i];
        }
        // Shuffle the sampled result so output order is unpredictable
        Random.Shared.Shuffle(sampled);
        return sampled.Select(c => c.ToString()).ToArray();
    }

    private async Task<Guid> ResolveWordsetIdAsync(string shareCode, CancellationToken ct)
    {
        var cached = shareCodeCache.TryGet(shareCode);
        if (cached.HasValue) return cached.Value;

        var wordset = await wordsetRepository.GetByShareCodeAsync(shareCode, ct)
            ?? throw new WordsetNotFoundException(shareCode);

        shareCodeCache.Set(shareCode, wordset.Id);
        return wordset.Id;
    }
}
