using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Wordset.Domain.Interfaces;
using Wordset.Infrastructure.Data;

namespace Wordset.Infrastructure.Repositories;

public class WordsetRepository(WordsetDbContext db) : IWordsetRepository
{
    public Task<Domain.Entities.Wordset?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Wordsets.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<Domain.Entities.Wordset?> GetByShareCodeAsync(string shareCode, CancellationToken ct = default)
        => db.Wordsets.FirstOrDefaultAsync(w => w.ShareCode == shareCode, ct);

    public async Task<IReadOnlyList<Domain.Entities.Wordset>> GetWordsetPageAsync(string? afterShareCode, int pageSize, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.Wordset> query = afterShareCode is null
            ? db.Wordsets.FromSqlInterpolated(
                $"SELECT id, name, share_code, created_at, updated_at FROM wordsets ORDER BY share_code")
            : db.Wordsets.FromSqlInterpolated(
                $"SELECT id, name, share_code, created_at, updated_at FROM wordsets WHERE share_code > {afterShareCode} ORDER BY share_code");

        return await query.Take(pageSize).ToListAsync(ct);
    }

    public async Task<Domain.Entities.Wordset> CreateAsync(Domain.Entities.Wordset wordset, CancellationToken ct = default)
    {
        db.Wordsets.Add(wordset);
        await db.SaveChangesAsync(ct);
        await ProvisionPartitionAsync(wordset.Id, ct);
        return wordset;
    }

    public async Task<Domain.Entities.Wordset> UpdateAsync(Domain.Entities.Wordset wordset, CancellationToken ct = default)
    {
        db.Wordsets.Update(wordset);
        await db.SaveChangesAsync(ct);
        return wordset;
    }

    public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken ct = default)
        => db.Wordsets.AnyAsync(w => w.ShareCode == shareCode, ct);

    public Task<int> GetWordCountAsync(Guid wordsetId, CancellationToken ct = default)
        => db.Words.CountAsync(w => w.WordsetId == wordsetId, ct);

    public async Task<IReadOnlyList<string>> GetWordsPageAsync(Guid wordsetId, string? afterWord, int pageSize, CancellationToken ct = default)
    {
        // FromSqlInterpolated parameterizes all values; compose with Take so EF Core wraps in a subquery.
        IQueryable<Domain.Entities.Word> query = afterWord is null
            ? db.Words.FromSqlInterpolated(
                $"SELECT wordset_id, word, frequency_rank FROM words WHERE wordset_id = {wordsetId} ORDER BY word")
            : db.Words.FromSqlInterpolated(
                $"SELECT wordset_id, word, frequency_rank FROM words WHERE wordset_id = {wordsetId} AND word > {afterWord} ORDER BY word");

        return await query
            .Take(pageSize)
            .Select(w => w.Value)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, int?>> GetWordFrequencyMapAsync(Guid wordsetId, CancellationToken ct = default)
        => await db.Words
            .Where(w => w.WordsetId == wordsetId)
            .ToDictionaryAsync(w => w.Value, w => w.FrequencyRank, ct);

    public async Task ReplaceWordsAsync(Guid wordsetId, IReadOnlyList<Domain.Entities.Word> words, CancellationToken ct = default)
    {
        var partitionName = PartitionName(wordsetId);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // EF1002: partition name is Guid-derived (hex only) — no SQL injection surface.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"TRUNCATE {partitionName}", ct);
#pragma warning restore EF1002

        if (words.Count > 0)
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();

            // Binary COPY streams all rows in a single round-trip — O(n) vs EF Core's per-row INSERT.
            await using var writer = await connection.BeginBinaryImportAsync(
                "COPY words (wordset_id, word, frequency_rank) FROM STDIN (FORMAT BINARY)", ct);

            foreach (var word in words)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(word.WordsetId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(word.Value, NpgsqlDbType.Text, ct);
                if (word.FrequencyRank.HasValue)
                    await writer.WriteAsync(word.FrequencyRank.Value, NpgsqlDbType.Integer, ct);
                else
                    await writer.WriteNullAsync(ct);
            }

            await writer.CompleteAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private async Task ProvisionPartitionAsync(Guid wordsetId, CancellationToken ct)
    {
        var partitionName = PartitionName(wordsetId);
        var idLiteral = wordsetId.ToString();

        // EF1002: values are Guid-derived (hex + hyphens only) — no SQL injection surface.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TABLE {partitionName}
                PARTITION OF words
                FOR VALUES IN ('{idLiteral}')
            """, ct);

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX ON {partitionName} (word)", ct);

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE INDEX ON {partitionName} USING gist (word gist_trgm_ops)", ct);
#pragma warning restore EF1002
    }

    public static string PartitionName(Guid wordsetId) => $"words_{wordsetId:N}";
}
