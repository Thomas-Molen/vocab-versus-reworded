namespace Wordset.Application.Models;

public record WordsetDto(
    string ShareCode,
    string Name,
    int WordCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
