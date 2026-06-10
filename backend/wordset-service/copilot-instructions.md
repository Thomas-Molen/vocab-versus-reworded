# wordset-service — Copilot Instructions

## Service Overview

.NET 10 ASP.NET Core minimal API service. Manages community wordlists stored in PostgreSQL with `pg_trgm` and LIST partitioning. The game-engine calls this service to validate player word submissions.

## Build & Test

```bash
dotnet run
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~SomeTest"   # single test
```

Requires PostgreSQL. Use `docker compose up postgres` from the repo root to start only the database.

## Database: Partitioned Words Table

The `words` table is **LIST-partitioned by `wordset_id`**. Never query the parent `words` table directly in application code — always query the partition (which happens automatically when you filter by `wordset_id`).

### Partition lifecycle

- **Create wordset** → execute `CREATE TABLE words_<id_no_hyphens> PARTITION OF words FOR VALUES IN ('<id>')` then create indexes
- **Delete wordset** → execute `DROP TABLE words_<id_no_hyphens>` (cascades all rows and indexes)

This DDL is **not** managed by EF Core migrations — execute it via `DbContext.Database.ExecuteSqlRawAsync(...)`. EF Core migrations only manage `wordsets` and the parent `words` table structure.

### Index per partition

Each partition gets two indexes created immediately after the partition is provisioned:

```sql
CREATE UNIQUE INDEX ON words_<id> (word);
CREATE INDEX ON words_<id> USING gist (word gist_trgm_ops);
```

### Partition naming

`words_` + wordset UUID with hyphens removed.

| UUID | Partition name |
|------|---------------|
| `550e8400-e29b-41d4-a716-446655440000` | `words_550e8400e29b41d4a716446655440000` |

Helper: `"words_" + wordsetId.ToString("N")` (the `"N"` format specifier removes hyphens in .NET).

## Conventions

- Minimal APIs (`MapGet`, `MapPost`, `MapDelete`) — no controllers
- EF Core for `wordsets` CRUD; raw SQL for all partition DDL
- Words are **always stored and compared lowercase** — normalise on input
- `frequency_rank`: nullable int, populated at import time only. Never written during gameplay. Lower value = more commonly used word.
- `IOptions<T>` for all configuration — no raw `IConfiguration` access in service classes
- `CancellationToken` threaded through all async call chains

## Word Validation Logic

1. Exact match: query the partition for `word = @word` (case-insensitive via stored lowercase)
2. Fuzzy match (optional, when `fuzzy: true`): use `word % @word` with the `pg_trgm` similarity operator. Return the closest match and similarity score.

## Letter Combination Derivation

The `/wordsets/{id}/letter-combinations` endpoint returns all character sequences (1-3 chars) that appear as a substring in at least one word in the wordset. The game-engine uses this to generate solvable rounds.

This query runs against the wordset's partition. It may be slow on very large wordlists — consider caching the result (invalidate on word add/remove) in Phase 2.

## README

Keep `README.md` in this directory up to date when:
- Adding or removing REST endpoints (update the endpoint table)
- Changing the database schema (update the schema block)
- Changing partition naming conventions
- Adding new configuration keys or environment variables
