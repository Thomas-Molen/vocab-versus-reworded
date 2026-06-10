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

## Architecture: Onion Layers

```
wordset-service.Domain/        ← entities, IWordsetRepository, domain exceptions
wordset-service.Application/   ← WordsetService (use cases), DTOs, mappers
wordset-service.Infrastructure/← WordsetDbContext (EF Core + Npgsql), WordsetRepository
wordset-service.API/           ← Program.cs, minimal API endpoints, DI wiring (startup project)
wordset-service.Tests/         ← xUnit unit tests only (Unit/ subfolder)
```

`Domain` has no project dependencies. `Infrastructure` and `Application` depend on `Domain`. `API` depends on `Application` + `Infrastructure`.

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

## Build Configuration

Shared build properties (`TargetFramework`, `Nullable`, `ImplicitUsings`) live in `Directory.Build.props` at the service root — do not repeat them in individual `.csproj` files.

## Suppressing Warnings

Suppress warnings at the call site with `#pragma warning disable <code>` (not globally in `.csproj`). Always include a one-line comment explaining why the suppression is justified:

```csharp
// EF1002: values are Guid-derived (hex + hyphens only) — no SQL injection surface.
#pragma warning disable EF1002
await db.Database.ExecuteSqlRawAsync($"...", ct);
#pragma warning restore EF1002
```

## Conventions

- Minimal APIs (`MapGet`, `MapPost`, `MapDelete`, `MapPatch`, `MapPut`) in `API` project — no controllers
- All wordset routes use `{shareCode}` (6-char string) as the public identifier — never `{id:guid}` in URLs. The Guid `id` is the internal DB key only.
- **OpenAPI documentation lives in the endpoint registration**, not in the README. Use `.WithSummary("...")` and `.WithDescription("...")` on each `MapGet/Post/etc` call, and `[Description("...")]` (from `System.ComponentModel`) on individual handler parameters. The README endpoint table is a quick reference only — do not add prose descriptions of endpoints there.
- EF Core for `wordsets` CRUD; raw SQL for all partition DDL
- Words are **always stored and compared lowercase** — normalise on input
- `frequency_rank`: nullable int, populated at import time only. Never written during gameplay.
- `wordsets.name` is **not unique** — multiple wordsets can share a name. `share_code` is the unique human-readable discriminator.
- `share_code`: 6-character uppercase alphanumeric (charset: `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`), generated at creation, immutable, DB unique constraint.
- `words` table composite PK is `(wordset_id, word)` — no separate `id` column
- `IOptions<T>` for all configuration — no raw `IConfiguration` access in service classes
- `CancellationToken` threaded through all async call chains

## Word Management

Words are managed via `PUT /wordsets/{id}/words` — a full-replacement endpoint, not individual add/remove operations. The request body is `{ "words": ["word1", "word2", ...] }`.

- Words are normalised to lowercase and deduplicated before storage
- Existing `frequency_rank` is preserved for words that survive the replacement; new words receive `frequency_rank = 0`
- The endpoint is designed for large batches (thousands of words) — internally uses Npgsql binary COPY for O(n) insert performance
- Returns the updated `WordsetDto` with the new `wordCount`

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
