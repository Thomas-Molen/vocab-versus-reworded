# wordset-service

REST API for managing community wordlists and validating submitted words. Backed by PostgreSQL with `pg_trgm` for fuzzy matching and LIST partitioning for per-wordset isolation.

## Running

```bash
# From this directory:
dotnet run

# Run all tests:
dotnet test

# Run a single test:
dotnet test --filter "FullyQualifiedName~SomeTest"

# Build only:
dotnet build
```

Default port: `8080` (mapped to `5001` in docker-compose).

Requires a running PostgreSQL instance. Set the connection string via environment variable or `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=wordsets;Username=vocabversus;Password=vocabversus"
  }
}
```

## REST Endpoints

### Wordsets

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/wordsets` | List all wordsets (id, name, word count) |
| `POST` | `/wordsets` | Create a new wordset. Body: `{ "name": "..." }` |
| `GET` | `/wordsets/{id}` | Get wordset metadata |
| `DELETE` | `/wordsets/{id}` | Delete wordset and its partition |

### Words

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/wordsets/{id}/words` | Add words. Body: `{ "words": ["...", "..."] }` |
| `DELETE` | `/wordsets/{id}/words` | Remove words. Body: `{ "words": ["...", "..."] }` |
| `POST` | `/wordsets/{id}/validate` | Validate a word. Body: `{ "word": "...", "fuzzy": false }` |
| `GET` | `/wordsets/{id}/letter-combinations` | Get all valid playable letter combinations (1-3 chars) |

## Database Schema

```sql
CREATE TABLE wordsets (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL UNIQUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- LIST-partitioned by wordset_id; never queried directly
CREATE TABLE words (
    wordset_id      UUID NOT NULL,
    word            TEXT NOT NULL,
    frequency_rank  INT,          -- NULL = unknown; lower = more common
    PRIMARY KEY (wordset_id, word)
) PARTITION BY LIST (wordset_id);
```

Each wordset partition is named `words_<wordset_id_no_hyphens>` and has:
- A unique index on `word`
- A `pg_trgm` GiST index for fuzzy matching

See [ADR-003](../../ADRs/ADR-003-word-store.md) for the full rationale.

## Partition Management

Partition DDL (CREATE/DROP) is executed via raw SQL — EF Core migrations do not manage partitions. The service handles this automatically in the wordset create/delete endpoints.

Partition naming convention: `words_` + wordset UUID without hyphens.

Example: wordset `550e8400-e29b-41d4-a716-446655440000` → partition `words_550e8400e29b41d4a716446655440000`.
