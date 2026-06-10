using Wordset.Application.Models;

namespace Wordset.Application.Mappers;

internal static class WordsetMapper
{
    public static WordsetDto ToDto(this Domain.Entities.Wordset wordset, int wordCount) =>
        new(wordset.ShareCode, wordset.Name, wordCount, wordset.CreatedAt, wordset.UpdatedAt);
}
