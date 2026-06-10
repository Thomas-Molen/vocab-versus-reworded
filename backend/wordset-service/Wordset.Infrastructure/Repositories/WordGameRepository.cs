using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wordset.Domain.Interfaces;
using Wordset.Infrastructure.Data;
using Wordset.Infrastructure.Repositories;

namespace Wordset.Infrastructure.Repositories;

public class WordGameRepository(WordsetDbContext db) : IWordGameRepository
{
    public async Task<IReadOnlyList<string>> GetRandomWordsAsync(Guid wordsetId, int count, CancellationToken ct = default)
        => await db.Words
            .FromSqlInterpolated(
                $"SELECT wordset_id, word, frequency_rank FROM words WHERE wordset_id = {wordsetId} ORDER BY random()")
            .Select(w => w.Value)
            .Take(count)
            .ToListAsync(ct);

    public async Task<string?> ExactMatchAsync(Guid wordsetId, string normalizedWord, CancellationToken ct = default)
        => await db.Words
            .Where(w => w.WordsetId == wordsetId && w.Value == normalizedWord)
            .Select(w => w.Value)
            .FirstOrDefaultAsync(ct);

    public async Task<string?> FuzzyMatchAsync(Guid wordsetId, string normalizedWord, int maxDistance, CancellationToken ct = default)
    {
        // Use trigram similarity as a pre-filter, then apply levenshtein for precise distance.
        // Queries the parent 'words' table — PostgreSQL routes to the correct partition via wordset_id.
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await connection.OpenAsync(ct);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT word
                FROM words
                WHERE wordset_id = $1
                  AND similarity(word, $2) > 0.2
                  AND levenshtein(word, $2) <= $3
                ORDER BY levenshtein(word, $2) ASC, similarity(word, $2) DESC
                LIMIT 1";

            cmd.Parameters.Add(new NpgsqlParameter { Value = wordsetId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = normalizedWord });
            cmd.Parameters.Add(new NpgsqlParameter { Value = maxDistance });

            var result = await cmd.ExecuteScalarAsync(ct);
            return result as string;
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }
}
