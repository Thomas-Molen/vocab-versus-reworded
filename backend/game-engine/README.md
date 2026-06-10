# game-engine

Real-time game server for VocabVersus Reworded. Manages game instances, player sessions, and round logic. Exposes a SignalR hub for real-time communication and a minimal REST API for game creation.

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

Default port: `8080` (mapped to `5000` in docker-compose).

## REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/games` | Create a new game instance. Returns `gameId` and game config. |

## SignalR Hub — `/gamehub`

### Client → Server (invoke)

| Method | Parameters | Description |
|--------|-----------|-------------|
| `CheckGame` | `gameId`, `sessionToken` | Validate game exists, register session token |
| `Join` | `gameId`, `nickname` | Join game with display name |
| `Reconnect` | — | Restore session after disconnect using stored token |
| `Ready` | `isReady` | Toggle ready state; game starts when all players ready |
| `Submit` | `word` | Submit a word for the current round |

### Server → Client (receive)

| Event | Payload | Description |
|-------|---------|-------------|
| `SubmitResult` | `{ isCorrect }` | Immediate feedback to submitting player |
| `StartRound` | `{ requiredCharacters, roundStartTimestamp }` | New round begins |
| `RoundEnding` | `{ endTimestamp }` | First correct answer — countdown start |
| `AddPoints` | `{ playerId, points }` | Points awarded this round |
| `UserJoined` | `{ playerId, nickname }` | Player joined |
| `UserLeft` | `{ playerId }` | Player disconnected |
| `UserReconnected` | `{ playerId }` | Player reconnected |
| `GameStateChanged` | `{ state }` | State transition: `Lobby`, `Starting`, `Started` |
| `GameStarting` | `{ startTimestamp }` | Countdown to game start |

## Architecture

Game state is **in-memory** per process:

- **`GameInstanceCache`** (`ConcurrentDictionary<string, GameInstance>`) — keyed by game ID
- **`SessionTokenCache`** (`ConcurrentDictionary<string, PlayerSession>`) — keyed by session token (UUID from client `localStorage`)

Each `GameInstance` owns a `Channel<GameEvent>` and a dedicated background `Task` that processes events sequentially. SignalR hub methods **only enqueue events** — they never mutate game state directly. See [ADR-005](../../ADRs/ADR-005-realtime-communication.md).

Session tokens survive SignalR reconnects (new `ConnectionId`). See [ADR-004](../../ADRs/ADR-004-player-identity.md).

## Scoring

Calculated at round-end (not during the submit hot path):

```
total_points = speed_bonus + word_rarity_bonus - wrong_guess_penalty
```

- `speed_bonus` — time delta between round start timestamp and correct submission
- `word_rarity_bonus` — derived from `frequency_rank` fetched from wordset-service (`NULL` rank = no bonus)
- `wrong_guess_penalty` — deducted per incorrect submission in the round

## Configuration

| Key | Description | Default |
|-----|-------------|---------|
| `Services:WordsetServiceUrl` | Base URL for wordset-service | `http://localhost:5001` |
| `Game:RoundEndDelay` | Seconds from first correct answer to next round | `10` |
| `Game:MaxPlayers` | Maximum players per game | `8` |
