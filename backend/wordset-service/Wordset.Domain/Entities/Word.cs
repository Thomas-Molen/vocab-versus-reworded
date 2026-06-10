namespace Wordset.Domain.Entities;

/// <summary>
/// Represents a word in a wordset partition. The composite PK is (WordsetId, Word).
/// This entity maps to the parent partitioned 'words' table — PostgreSQL routes
/// queries and inserts to the correct partition automatically based on WordsetId.
/// </summary>
public class Word
{
    public Guid WordsetId { get; init; }
    public required string Value { get; init; }
    public int? FrequencyRank { get; init; }
}
