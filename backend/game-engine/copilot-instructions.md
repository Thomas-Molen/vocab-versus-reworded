# game-engine — Copilot Instructions

## Service Overview

.NET 10 ASP.NET Core service. Handles all real-time game logic via a SignalR `GameHub` and exposes a single REST endpoint (`POST /games`) for game creation. All game state is in-memory.

## Build & Test

```bash
dotnet run
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~SomeTest"   # single test
```

## Key Architecture: Channel-per-GameInstance

Each `GameInstance` owns a `System.Threading.Channels.Channel<GameEvent>` and a dedicated background `Task` (started when the instance is created). **SignalR hub methods must only enqueue events into the channel** — they must never read or mutate `GameInstance` state directly.

The channel processor runs sequentially, so game-state mutations inside the processor need no locks.

```
SignalR Hub method
  └─► channel.Writer.TryWrite(new GameEvent(...))   ← fast, non-blocking
          ↓ (separate Task)
  GameInstance processor
    └─► reads event, mutates state, calls Clients.Group(...)
```

`GameInstanceCache` and `SessionTokenCache` are `ConcurrentDictionary<>` singletons — thread-safe for concurrent lookups from multiple hub connections.

## Adding a New Game Event

1. Add a case to the `GameEvent` discriminated union (or record type)
2. Add the hub method that enqueues the event — keep the hub method thin
3. Add handling in the `GameInstance` channel processor
4. Add the corresponding client-side event name as a constant
5. Update `README.md` with the new hub method / server event in the tables

## Conventions

- Minimal APIs for REST (`MapPost`, `MapGet`) — no controllers
- All services registered in `Program.cs` via `builder.Services.Add...`
- Configuration bound with `IOptions<T>` — define a settings class, not raw `IConfiguration` access
- `CancellationToken` threaded through all async call chains
- Typed `HttpClient` for the wordset-service call (registered as `IWordsetServiceClient`)
- All words passed to wordset-service are lowercased before sending

## Session Token Flow

1. Client sends token on `CheckGame` → server registers `token → PlayerSession` in `SessionTokenCache`
2. Client sends token on `Join` → server looks up session, sets nickname, adds player to game
3. On `Reconnect` → server looks up session by token (survives new `ConnectionId`)

Never trust the SignalR `Context.ConnectionId` as a stable player identifier — always look up via session token.

## Scoring (round-end only)

Scoring runs after a round ends, not during submission processing. The formula:

```
total = speed_bonus + word_rarity_bonus - wrong_guess_penalty
```

`frequency_rank` is fetched from the wordset-service at round-end — it is not cached in the game instance during the round.

## README

Keep `README.md` in this directory up to date when:
- Adding or removing REST endpoints (update the endpoint table)
- Adding or removing SignalR hub methods or server events (update both tables)
- Adding new configuration keys (update the configuration table)
- Changing the default port or startup behaviour
