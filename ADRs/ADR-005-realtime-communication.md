# ADR-005: SignalR for real-time game communication

**Status:** Accepted  
**Date:** 2026-06-08

## Context

VocabVersus is a real-time multiplayer game. The following events must be pushed to all connected players with minimal latency:

- Letter set revealed (all players must see this **simultaneously**)
- Word submission result (feedback to submitting player **immediately**)
- First correct answer triggers a countdown timer (broadcast to all)
- Score updates (pushed to all players as they happen)
- Player join/leave/ready state changes

The game is **server-authoritative**: the server validates submissions, manages round state, and is the single source of truth for timing. Clients should not be able to manipulate game state by replaying messages or faking events.

## Decision

Use **ASP.NET Core SignalR** for all real-time communication between game clients and the game engine.

A single `GameHub` (inheriting from `Microsoft.AspNetCore.SignalR.Hub`) handles all game events. Players are grouped by game instance using SignalR's built-in **Groups** abstraction (`Groups.AddToGroupAsync`), enabling efficient broadcast to all players in a game without iterating over individual connections.

### Timing strategy

All timing-sensitive broadcasts include a **UTC Unix millisecond timestamp** computed on the server:

```csharp
var startTime = DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds();
await Clients.Group(gameId).SendAsync("GameStarting", startTime);
```

Clients use this absolute timestamp rather than a relative delay, so network jitter does not cause one player's timer to start earlier than another's.

### Hub method summary

| Method | Direction | Description |
|--------|-----------|-------------|
| `CheckGame` | Client → Server → Client | Validate game exists, register session token |
| `Join` | Client → Server → Group | Join game with nickname |
| `Reconnect` | Client → Server → Group | Restore session after disconnect |
| `Ready` | Client → Server → Group | Toggle ready state; triggers game start when all ready |
| `Submit` | Client → Server | Submit a word for the current round |
| `SubmitResult` | Server → Caller | Immediate feedback: correct/incorrect |
| `StartRound` | Server → Group | New round begins, includes letter set |
| `RoundEnding` | Server → Group | First correct answer — countdown timestamp |
| `AddPoints` | Server → Group | Points awarded, player ID and amount |
| `UserJoined` | Server → Group | Player joined notification |
| `UserLeft` | Server → Group | Player disconnected notification |
| `GameStateChanged` | Server → Group | Game state transition (lobby/starting/started) |

## Consequences

**Positive:**
- SignalR handles WebSocket, Server-Sent Events, and Long Polling transports transparently — clients on different network conditions still work
- Built-in Groups abstraction maps naturally to game rooms
- Strong integration with ASP.NET Core DI, authentication middleware, and .NET async patterns
- Client library (`@microsoft/signalr`) is mature and actively maintained

**Negative:**
- SignalR connections are stateful — horizontal scaling requires a **backplane** (e.g. Redis or Azure SignalR Service) to route messages across multiple server instances. This is deferred to Phase 2.
- `ConnectionId` changes on reconnect, requiring the session-token identity layer (see ADR-004) to maintain player continuity

## Alternatives Considered

- **Raw WebSockets**: More control, less overhead, but SignalR's Groups, reconnect handling, and typed client generation add enough value to justify it
- **Server-Sent Events (SSE)**: Unidirectional (server → client only) — cannot handle client submissions without a separate REST channel, complicating the architecture
- **gRPC streaming**: Strong typing and efficient serialisation, but poor browser support for bidirectional streams without a proxy layer
