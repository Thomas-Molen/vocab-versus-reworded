# ADR-006: gRPC for wordset gameplay hot-path operations

**Status:** Accepted  
**Date:** 2026-06-10

## Context

During active gameplay the game-engine calls the wordset-service for two operations on every round:

1. **GetChallenge** — produce a set of N distinct letters that is guaranteed to be solvable against a given wordset (i.e. at least one word in the wordset contains all of those characters)
2. **ValidateWord** — check whether a player's submitted word exists in the wordset, with optional fuzzy tolerance

Both are latency-sensitive. ValidateWord in particular fires on every player submission during a live round. REST + JSON over HTTP/1.1 is sufficient for low-frequency management calls (wordset CRUD) but adds unnecessary overhead here:

- JSON serialization/deserialization cost per request
- HTTP/1.1 request overhead (headers, connection management)
- Text encoding of what are structurally simple typed messages

Alternatives evaluated:

| Approach | Exact-match validation latency | Notes |
|----------|-------------------------------|-------|
| REST (existing) | Low–medium | Fine for management, unnecessary overhead for hot path |
| Load wordset into game-engine memory (`HashSet<string>`) | Minimal (in-process) | Zero network per validation, but memory multiplied by concurrent games × wordset size; game-engine must manage cache invalidation if wordset changes during a live game |
| gRPC | Very low | Binary Protobuf + HTTP/2 multiplexing; typed contract; wordset-service remains the single authority |
| SignalR (hub-to-hub) | — | SignalR is designed for server→client push, not request/response between services |
| Message queue | — | Not suitable for synchronous request/response |

## Decision

Expose a **gRPC service** (`WordsetGameService`) on wordset-service alongside the existing REST API. The game-engine calls it via a typed gRPC client for both hot-path operations.

### Proto contract

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
  string letters = 1;  // shuffled distinct characters, e.g. "TRA"
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

### Challenge generation strategy

An explicit list of **excluded characters** is maintained as a server-side constant in `WordGameService` (initial list: `[' ', '-']`). These characters are never returned as challenge letters and are stripped from words before character sampling.

Steps:
1. Select a random word from the wordset partition (`ORDER BY random() LIMIT 1`) whose count of distinct non-excluded characters is ≥ `letter_count`. Retry (or return `FAILED_PRECONDITION`) if no qualifying word exists.
2. Strip excluded characters from the selected word.
3. Sample `letter_count` distinct characters at random from the remaining characters.
4. Return them in **shuffled order** — not alphabetically sorted, so the order gives no hint about the source word.

### ValidateWord implementation

**Excluded character normalisation** is applied to all validation, regardless of `fuzzy_tolerance`. Before comparison, strip all excluded characters (same list as GetChallenge) from the submitted word. Stored words are compared after the same normalisation. This means spaces are always ignored — "ice cream" submitted matches "icecream" stored and vice versa.

- **Exact path** (`fuzzy_tolerance = 0`): normalise → direct PK lookup `(wordset_id, normalised_word)` — O(log n)
- **Fuzzy path** (`fuzzy_tolerance > 0`): normalise → use `pg_trgm` similarity as a pre-filter to narrow candidates, then apply `fuzzystrmatch.levenshtein()` to find the closest match within `fuzzy_tolerance`. The trigram GiST index cannot accelerate Levenshtein directly, but the trigram pre-filter substantially reduces the candidate set.

`ValidateWordResponse.matched_word` returns the stored word that was matched (post-normalisation). On no match it is empty string.

### Onion layer placement

| Concern | Layer | Location |
|---------|-------|----------|
| gRPC transport | API | `Wordset.API/GrpcServices/WordsetGameGrpcService.cs` |
| Challenge + validation business logic | Application | `Wordset.Application/Services/WordGameService.cs` |
| Repository contract | Domain | `Wordset.Domain/Interfaces/IWordGameRepository.cs` |
| Repository implementation | Infrastructure | `Wordset.Infrastructure/Repositories/WordGameRepository.cs` |
| Proto definition | API | `Wordset.API/Protos/wordset_game.proto` |

The gRPC service implementation (`WordsetGameGrpcService`) translates gRPC request messages into application-layer service calls and maps responses back to Protobuf messages. Business logic stays in `WordGameService`; the gRPC class contains no logic.

### NuGet packages required

- `Grpc.AspNetCore` (server-side, added to `Wordset.API`)
- `Grpc.Net.Client` + `Google.Protobuf` + `Grpc.Tools` (client-side, added to `game-engine`)

### Port configuration

Kestrel can serve both REST (HTTP/1.1) and gRPC (HTTP/2) on different ports. In development:
- REST: `http://localhost:8080` (existing)
- gRPC: `http://localhost:8090` (new, HTTP/2 cleartext for internal calls)

In `docker-compose.yml`, expose `8090` as the internal gRPC port.

### share_code → Guid cache

Resolving `share_code → Guid` requires a DB lookup on every gRPC call. Since share_codes are immutable, this mapping can be cached in memory.

- **Implementation**: `IShareCodeCache` singleton registered in DI; backed by `IMemoryCache` with a **5-minute sliding expiration**.
- **Population**: lazy — on first lookup, cache miss triggers a DB query and populates the entry with the sliding TTL.
- **Invalidation**: sliding expiration handles cleanup naturally. Entries that haven't been requested for 5 minutes are evicted automatically. No explicit eviction on wordset delete is required — a deleted wordset would return `NOT_FOUND` at the DB level on the next cache miss anyway.
- **Scope**: `IShareCodeCache` is registered as a singleton and injected into both `WordGameRepository` (gRPC hot-path) and `WordsetRepository` (REST paths) to share the same underlying cache.

## Consequences

**Positive:**
- ValidateWord and GetChallenge are now low-latency binary RPC calls
- Typed, versioned contract via `.proto` — breaking changes are explicit
- wordset-service remains the single authority for word data
- HTTP/2 multiplexing handles concurrent validation calls efficiently during multi-player rounds
- No memory multiplication in game-engine from loading word lists

**Negative:**
- Additional `Grpc.AspNetCore` dependency in wordset-service
- Proto files must be kept in sync between producer and consumer (can be managed via a shared NuGet package or file copy in CI)
- gRPC over HTTP/2 cleartext requires explicit Kestrel configuration (no TLS on internal network — acceptable for a local/internal service mesh)
- `ORDER BY random()` for challenge generation is O(n) for large partitions. For wordsets with 100k+ words this may be slow; consider a reservoir-sampling or count-then-offset approach if profiling shows it is a bottleneck.

## Alternatives Considered

- **In-memory cache in game-engine**: Rejected as primary approach. Fast for validation but requires game-engine to manage cache invalidation (wordsets can be edited), multiplies memory usage across concurrent games, and couples game-engine to wordset data layout. Could be added as a future optimisation layer on top of gRPC.
- **REST (keep existing)**: Rejected for hot path. Adequate for management operations; JSON + HTTP/1.1 overhead is unnecessary when a typed binary protocol is available.
- **Redis**: Would require a third infrastructure dependency (Redis in addition to PostgreSQL). Adds operational complexity. The performance benefit over gRPC + PostgreSQL PK lookup is marginal for this use case.
