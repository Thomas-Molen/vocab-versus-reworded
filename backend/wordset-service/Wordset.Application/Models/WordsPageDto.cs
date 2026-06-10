namespace Wordset.Application.Models;

public record WordsPageDto(
    IReadOnlyList<string> Words,
    string? NextCursor
);
