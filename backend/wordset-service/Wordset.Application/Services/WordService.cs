using System.Text;
using Wordset.Application.Mappers;
using Wordset.Application.Models;
using Wordset.Domain.Exceptions;
using Wordset.Domain.Interfaces;

namespace Wordset.Application.Services;

public class WordService(IWordsetRepository repository)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    public async Task<WordsPageDto> GetWordsAsync(string shareCode, string? cursor, int? pageSize, CancellationToken ct = default)
    {
        var wordset = await ResolveAsync(shareCode, ct);
        var effectivePageSize = ValidatePageSize(pageSize, DefaultPageSize, MaxPageSize);

        string? afterWord = null;
        if (cursor is not null)
        {
            try
            {
                afterWord = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid cursor.", nameof(cursor));
            }
        }

        // Fetch one extra row to determine if more pages exist
        var rows = await repository.GetWordsPageAsync(wordset.Id, afterWord, effectivePageSize + 1, ct);

        var hasMore = rows.Count > effectivePageSize;
        var words = hasMore ? rows.Take(effectivePageSize).ToList() : (IReadOnlyList<string>)rows;
        var nextCursor = hasMore
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(words[^1]))
            : null;

        return new WordsPageDto(words, nextCursor);
    }

    public async Task<WordsetDto> ReplaceWordsAsync(string shareCode, ReplaceWordsRequest request, CancellationToken ct = default)
    {
        var wordset = await ResolveAsync(shareCode, ct);

        var normalized = request.Words
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 0)
            .Distinct()
            .ToList();

        var existingRanks = await repository.GetWordFrequencyMapAsync(wordset.Id, ct);

        var words = normalized.Select(w => new Domain.Entities.Word
        {
            WordsetId = wordset.Id,
            Value = w,
            FrequencyRank = existingRanks.TryGetValue(w, out var rank) ? rank : 0,
        }).ToList();

        await repository.ReplaceWordsAsync(wordset.Id, words, ct);

        var wordCount = await repository.GetWordCountAsync(wordset.Id, ct);
        return wordset.ToDto(wordCount);
    }

    private async Task<Domain.Entities.Wordset> ResolveAsync(string shareCode, CancellationToken ct)
        => await repository.GetByShareCodeAsync(shareCode, ct)
            ?? throw new WordsetNotFoundException(shareCode);

    private static int ValidatePageSize(int? pageSize, int defaultSize, int maxSize)
    {
        var effective = pageSize ?? defaultSize;
        if (effective < 1 || effective > maxSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"pageSize must be between 1 and {maxSize}.");
        return effective;
    }
}
