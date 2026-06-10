# wordset-service — Copilot Instructions

## Service Overview

.NET 10 ASP.NET Core service. Manages community wordlists stored in PostgreSQL with `pg_trgm` and LIST partitioning. Exposes:
- **REST API** for wordset CRUD and word list management (HTTP/1.1, port 8080)
- **gRPC service** (`WordsetGameService`) for gameplay hot-path operations: word validation and challenge letter generation (HTTP/2, port 8090)

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
wordset-service.Domain/        ← entities, IWordsetRepository, IWordGameRepository, IShareCodeCache, domain exceptions
wordset-service.Application/   ← WordsetService, WordService, WordGameService (use cases), DTOs, mappers
wordset-service.Infrastructure/← WordsetDbContext (EF Core + Npgsql), WordsetRepository, WordGameRepository, ShareCodeCache
wordset-service.API/           ← Program.cs, minimal API endpoints, gRPC services, DI wiring (startup project)
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

Word validation is exposed via the gRPC `ValidateWord` RPC, not a REST endpoint.

**Excluded character normalisation** is applied to all validation before any comparison. Strip the excluded character list (same constant as GetChallenge: `[' ', '-']`) from the submitted word. Stored words are compared after the same strip. This means spaces are always ignored — "ice cream" submitted matches "icecream" stored.

- **Exact match** (`fuzzy_tolerance = 0`): normalise input → PK lookup `(wordset_id, normalised_word)` — O(log n)
- **Fuzzy match** (`fuzzy_tolerance > 0`): normalise input → use `pg_trgm` similarity as a pre-filter → apply `fuzzystrmatch.levenshtein()` to find the closest match within `fuzzy_tolerance`. The trigram GiST index does not accelerate Levenshtein directly, but trigram pre-filtering substantially reduces the candidate set.

`ValidateWordResponse.matched_word` is the stored word that was matched. Empty string when `valid = false`.

## Challenge Letter Generation

Challenge letters are exposed via the gRPC `GetChallenge` RPC.

**Excluded characters**: a server-side constant list in `WordGameService` (initial: `[' ', '-']`). These characters are never returned as challenge letters and are stripped from candidate words before sampling. The list can be extended without a proto change.

Strategy:
1. Select a random word from the partition whose count of distinct non-excluded characters is ≥ `letter_count`. Retry (or return `FAILED_PRECONDITION`) if no qualifying word exists.
2. Strip excluded characters from the selected word.
3. Sample `letter_count` distinct characters at random from the remaining characters.
4. Return them in **shuffled order** — not sorted, so the order gives no hint about the source word.

For very large partitions (100k+ words), `ORDER BY random()` performs a sequential scan; a count-then-offset strategy or reservoir sampling may be preferable if profiling shows it is a bottleneck.

## gRPC Service

The gameplay hot-path operations are exposed as a gRPC service alongside the REST API.

### Proto file

Location: `Wordset.API/Protos/wordset_game.proto`

```protobuf
syntax = "proto3";
option csharp_namespace = "Wordset.API.Protos";
package wordset_game;

service WordsetGameService {
  rpc GetChallenge (GetChallengeRequest) returns (GetChallengeResponse);
  rpc ValidateWord  (ValidateWordRequest)  returns (ValidateWordResponse);
}

message GetChallengeRequest {
  string share_code   = 1;
  int32  letter_count = 2;  // number of distinct challenge characters to return (e.g. 1–3)
}

message GetChallengeResponse {
  string letters = 1;  // shuffled distinct non-excluded characters, e.g. "TRA"
}

message ValidateWordRequest {
  string share_code      = 1;
  string word            = 2;
  int32  fuzzy_tolerance = 3;  // 0 = exact match only; >0 = Levenshtein distance allowed
}

message ValidateWordResponse {
  bool   valid        = 1;
  string matched_word = 2;  // the stored word that was matched; empty if valid = false
}
```

### Onion layer placement

| Concern | Layer | File |
|---------|-------|------|
| gRPC transport / message mapping | API | `Wordset.API/GrpcServices/WordsetGameGrpcService.cs` |
| Challenge + validation business logic | Application | `Wordset.Application/Services/WordGameService.cs` |
| Repository contract | Domain | `Wordset.Domain/Interfaces/IWordGameRepository.cs` |
| Repository implementation | Infrastructure | `Wordset.Infrastructure/Repositories/WordGameRepository.cs` |

`WordsetGameGrpcService` maps gRPC request messages to application service calls and maps responses back to Protobuf messages. It contains no business logic.

### Port configuration

Kestrel serves REST (HTTP/1.1) and gRPC (HTTP/2) on separate ports:
- REST: port `8080` (existing)
- gRPC: port `8090` (HTTP/2 cleartext — acceptable for internal service mesh)

### NuGet packages

Add `Grpc.AspNetCore` to `Wordset.API.csproj`. Regenerate gRPC stubs via `Grpc.Tools` — set `<Protobuf Include="Protos/wordset_game.proto" GrpcServices="Server" />` in the project file.

### Conventions

- The gRPC service uses `share_code` (the external identifier) as its input — consistent with the REST API. The service layer resolves `share_code → Guid` via a cache-backed lookup before calling `IWordGameRepository`.
- All gRPC inputs are validated in the application layer (empty `share_code`, empty `word`, out-of-range values). Return appropriate gRPC status codes (`NOT_FOUND`, `INVALID_ARGUMENT`) rather than throwing unhandled exceptions.
- Unit tests for `WordGameService` live in `Wordset.Tests/Unit/`. Stub `IWordGameRepository` manually — no mocking framework.

### share_code → Guid cache

Resolving `share_code → Guid` on every gRPC call would require a DB round-trip. An in-process cache eliminates this overhead:

- **Interface**: `IShareCodeCache` (Domain layer) — `TryGet(shareCode)`, `Set(shareCode, id)` 
- **Implementation**: `ShareCodeCache` (Infrastructure) — backed by `IMemoryCache` with a **5-minute sliding expiration**
- **Population**: lazy on first lookup; a cache miss triggers `IWordsetRepository.GetByShareCodeAsync` and populates the entry
- **Invalidation**: sliding expiration only — entries idle for 5 minutes are evicted automatically. No explicit eviction on wordset delete is needed (a deleted wordset returns `NOT_FOUND` at the DB level on the next cache miss)
- **Registration**: singleton in DI, injected into both `WordGameRepository` (gRPC hot-path) and `WordsetRepository` (REST paths) so all service calls share one cache instance

## README

Keep `README.md` in this directory up to date when:
- Adding or removing REST endpoints (update the endpoint table)
- Adding or removing gRPC RPCs (update the gRPC table)
- Changing the database schema (update the schema block)
- Changing partition naming conventions
- Adding new configuration keys or environment variables
- Changing the gRPC or REST port
