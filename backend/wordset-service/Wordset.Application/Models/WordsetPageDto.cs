namespace Wordset.Application.Models;

public record WordsetPageDto(
    IReadOnlyList<WordsetDto> Wordsets,
    string? NextCursor
);
