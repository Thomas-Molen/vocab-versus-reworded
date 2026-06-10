# VocabVersus Reworded — Copilot Instructions

## Project Overview

VocabVersus Reworded is a real-time multiplayer vocabulary game. Players are shown a set of 1-3 letters and must submit a word from the selected wordlist that contains those letters. Points are awarded based on speed, word rarity, and accuracy. The game is server-authoritative with real-time state broadcast via SignalR.

## Repository Structure

```
vocab-versus-reworded/            ← monorepo root
├── ADRs/                         ← Architecture Decision Records (read these first for decisions)
├── backend/
│   ├── game-engine/              ← .NET 10 ASP.NET Core: SignalR GameHub + REST game creation
│   └── wordset-service/          ← .NET 10 ASP.NET Core: wordlist CRUD + word validation REST API
├── frontend/                     ← Vite + React 18 + TypeScript SPA
└── docker-compose.yml            ← Full local stack
```

## Build & Run

### Full stack (local dev)
```bash
docker compose up
```

### Backend services (individually)
```bash
# From backend/game-engine or backend/wordset-service:
dotnet run
dotnet test                           # run all tests
dotnet test --filter "FullyQualifiedName~SomeTest"  # run a single test
dotnet build
```

### Frontend
```bash
# From frontend/:
npm install
npm run dev       # dev server with HMR
npm run build     # production build
npm run preview   # preview production build
npm test          # vitest unit tests
npm test -- SomeComponent  # run a single test file
```

## Architecture

### Services

**`backend/game-engine`** — the real-time game server
- SignalR `GameHub`: handles `CheckGame`, `Join`, `Reconnect`, `Ready`, `Submit`, and disconnect events
- REST: `POST /games` creates a game instance and returns a game ID + config
- All shared state lives in **in-memory singleton caches** using `ConcurrentDictionary`:
  - `GameInstanceCache`: `gameId → GameInstance`
  - `SessionTokenCache`: `sessionToken → PlayerSession`
- Each `GameInstance` owns a `System.Threading.Channels.Channel<GameEvent>` and runs a **dedicated background Task** as its event processor. SignalR handlers only enqueue events — they never mutate game state directly. This gives each game instance single-threaded sequential processing without blocking the hub.
- Calls `wordset-service` via a typed `HttpClient` to validate submitted words

**`backend/wordset-service`** — wordlist management and word validation
- REST API for wordset CRUD and word validation
- PostgreSQL backend with `pg_trgm` extension
- The `words` table is **LIST-partitioned by `wordset_id`**: each wordset has its own physical partition with its own `pg_trgm` GiST index. Creating a wordset provisions a new partition; deleting drops it.
- `frequency_rank` (int, nullable) is a static import-time column — never written during gameplay
- **Word management uses full-replacement semantics**: `PUT /wordsets/{id}/words` replaces the entire word list atomically. There are no separate add/remove endpoints. Words are normalised to lowercase and deduplicated. Existing `frequency_rank` values are preserved for surviving words; new words receive `frequency_rank = 0`. The endpoint uses Npgsql binary COPY for O(n) performance with large batches (thousands of words).

**`frontend`** — React SPA
- React Router v6 routes: `/` (home/create), `/lobby/:gameId`, `/game/:gameId`
- Session token (`crypto.randomUUID()`) generated on first load, stored in `localStorage` as `vv_session_token`
- All game state changes arrive via SignalR push events — do not poll REST endpoints during gameplay

### Real-time timing
All timing-sensitive broadcasts include a server-computed UTC Unix millisecond timestamp. Clients start timers based on the absolute timestamp, not a relative delay, so all players' timers are synchronised regardless of network jitter.

### Scoring (calculated at round-end, not during hot path)
`total_points = speed_bonus + word_rarity_bonus - wrong_guess_penalty`
- `speed_bonus`: based on time delta between round start and correct submission
- `word_rarity_bonus`: derived from `frequency_rank` in the wordset-service (lower rank = more common = lower bonus)
- `wrong_guess_penalty`: deducted per incorrect submission

## Key Conventions

### ADRs
All significant architectural decisions are documented in `ADRs/`. Before making a decision that affects technology choices, persistence strategy, communication protocols, or project structure, check for an existing ADR and create a new one if needed. ADRs follow the format: status, date, context, decision, consequences, alternatives considered.

### Backend (.NET)
- Minimal APIs pattern for REST endpoints (not controllers)
- **Onion architecture** — each backend service is split into four projects: `Domain` (entities, repository interfaces, domain exceptions), `Application` (use cases, DTOs, mappers), `Infrastructure` (EF Core, repository implementations), `API` (Program.cs, endpoints, DI wiring). `Domain` has no project dependencies; `API` depends on `Application` + `Infrastructure`.
- **One root class (or record/interface/enum) per `.cs` file** — no multiple top-level types in one file. `file`-scoped types count as a second root type and must live in their own file.
- **`Directory.Build.props`** at each service root holds shared build properties (`TargetFramework`, `Nullable`, `ImplicitUsings`) — never repeat them in individual `.csproj` files.
- **Suppress warnings at the call site** with `#pragma warning disable <code>` / `#pragma warning restore <code>`. Always include a one-line comment justifying the suppression. Never suppress globally in `.csproj` unless the warning originates from generated code outside our control.
- Dependency injection for all services; registered in `Program.cs`
- `IOptions<T>` for configuration binding
- Typed `HttpClient` registrations for inter-service calls
- All game-state mutations happen inside the `GameInstance` channel processor — never directly in hub methods
- Use `CancellationToken` propagation throughout async chains

### Testing (.NET)
- **xUnit** for all backend tests
- **Unit tests only** — test business logic in isolation. Do not test EF Core, ASP.NET, or any framework functionality.
- Stub `IRepository` interfaces manually when needed; no mocking framework required for simple stubs.
- Unit tests live in `*.Tests/Unit/`

### Frontend (React)
- SignalR connection lifecycle managed in a context provider (`SignalRContext`)
- Game state stored in React context/reducer, updated by SignalR event handlers
- Session token read/written via a single `useSessionToken()` hook
- TypeScript interfaces for all SignalR event payloads should mirror the server-side response models

### REST API Documentation (backend)
- **OpenAPI is the authoritative source for endpoint details.** Document endpoints directly in the endpoint registration using `.WithSummary("...")`, `.WithDescription("...")`, and `[Description("...")]` (from `System.ComponentModel`) on handler parameters. The OpenAPI spec is served at `/openapi/v1.json` when the service is running.
- **README endpoint tables are quick-reference only** — list the method, path, description, and body/query params in one row. Do not add prose descriptions of individual endpoints to the README.

### Database (wordset-service)
- EF Core for standard CRUD; raw SQL for partition management (CREATE/DROP TABLE)
- Words are always stored and compared lowercase
- Partition naming: `words_<wordset_id_without_hyphens>` — use `wordsetId.ToString("N")` in .NET
- `wordsets.name` is **not unique** — multiple wordsets can share the same name; `share_code` is the unique human-readable discriminator
- `share_code`: 6-character uppercase alphanumeric (charset: `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, no confusables), generated at creation, immutable, unique-constrained in DB
- `words` table uses composite PK `(wordset_id, word)` — no separate `id` column

## Keeping Documentation Up to Date

Each service has its own `README.md` and `copilot-instructions.md`. When making changes, update the README for the affected service.

All shell commands in READMEs assume the working directory is the **repo root**. Use repo-root-relative paths (e.g., `backend/wordset-service/Wordset.API`) so commands work without `cd`-ing first.

| Change type | Update |
|-------------|--------|
| REST endpoint added/removed/renamed | Service `README.md` endpoint table + OpenAPI annotations in the endpoint file |
| SignalR hub method or server event changed | `backend/game-engine/README.md` hub tables |
| New configuration key or environment variable | Service `README.md` configuration table |
| Database schema change | `backend/wordset-service/README.md` schema block |
| New frontend route | `frontend/README.md` routes table |
| New `localStorage` key | `frontend/README.md` |
| Cross-cutting architectural decision | New ADR in `ADRs/`, update root `README.md` if the service table changes |
