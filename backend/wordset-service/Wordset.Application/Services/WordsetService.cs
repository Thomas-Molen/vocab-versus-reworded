using System.Text;
using Wordset.Application.Mappers;
using Wordset.Application.Models;
using Wordset.Domain.Exceptions;
using Wordset.Domain.Interfaces;

namespace Wordset.Application.Services;

public class WordsetService(IWordsetRepository repository)
{
    // Characters used for share codes — no confusable O/0/I/1
    internal const string ShareCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    internal const int ShareCodeLength = 6;

    public const int DefaultWordsetPageSize = 20;
    public const int MaxWordsetPageSize = 100;

    public async Task<WordsetPageDto> GetWordsetPageAsync(string? cursor, int? pageSize, CancellationToken ct = default)
    {
        var effectivePageSize = ValidatePageSize(pageSize, DefaultWordsetPageSize, MaxWordsetPageSize);

        string? afterShareCode = null;
        if (cursor is not null)
        {
            try
            {
                afterShareCode = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid cursor.", nameof(cursor));
            }
        }

        var rows = await repository.GetWordsetPageAsync(afterShareCode, effectivePageSize + 1, ct);

        var hasMore = rows.Count > effectivePageSize;
        var wordsets = hasMore ? rows.Take(effectivePageSize).ToList() : (IReadOnlyList<Domain.Entities.Wordset>)rows;

        var dtos = new List<WordsetDto>(wordsets.Count);
        foreach (var ws in wordsets)
        {
            var count = await repository.GetWordCountAsync(ws.Id, ct);
            dtos.Add(ws.ToDto(count));
        }

        var nextCursor = hasMore
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(wordsets[^1].ShareCode))
            : null;

        return new WordsetPageDto(dtos, nextCursor);
    }

    public async Task<WordsetDto> GetByShareCodeAsync(string shareCode, CancellationToken ct = default)
    {
        var wordset = await ResolveAsync(shareCode, ct);
        var wordCount = await repository.GetWordCountAsync(wordset.Id, ct);
        return wordset.ToDto(wordCount);
    }

    public async Task<WordsetDto> CreateAsync(CreateWordsetRequest request, CancellationToken ct = default)
    {
        var shareCode = await GenerateUniqueShareCodeAsync(ct);

        var wordset = new Domain.Entities.Wordset
        {
            Name = request.Name.Trim(),
            ShareCode = shareCode,
        };

        var created = await repository.CreateAsync(wordset, ct);
        return created.ToDto(wordCount: 0);
    }

    public async Task<WordsetDto> UpdateAsync(string shareCode, UpdateWordsetRequest request, CancellationToken ct = default)
    {
        var wordset = await ResolveAsync(shareCode, ct);

        wordset.Name = request.Name.Trim();
        wordset.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateAsync(wordset, ct);
        var wordCount = await repository.GetWordCountAsync(wordset.Id, ct);
        return updated.ToDto(wordCount);
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

    private async Task<string> GenerateUniqueShareCodeAsync(CancellationToken ct)
    {
        string code;
        do
        {
            code = new string(Random.Shared.GetItems(ShareCodeChars.AsSpan(), ShareCodeLength));
        } while (await repository.ShareCodeExistsAsync(code, ct));

        return code;
    }

    // Exposed as internal for unit testing
    internal static bool IsValidShareCodeChar(char c) => ShareCodeChars.Contains(c);
}
