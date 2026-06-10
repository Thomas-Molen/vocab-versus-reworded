# wordset-service

REST API for managing community wordlists and validating submitted words. Backed by PostgreSQL with `pg_trgm` for fuzzy matching and LIST partitioning for per-wordset isolation.

## Prerequisites

This project is developed on **Windows via WSL2 and VS Code**. Before starting:

1. [Install WSL2](https://learn.microsoft.com/en-us/windows/wsl/install) with Ubuntu
2. [Install Docker Engine](https://docs.docker.com/engine/install/ubuntu/) inside the WSL2 distro
3. Install the recommended VS Code extensions (`.vscode/extensions.json`) — C# Dev Kit, Remote WSL, Docker

Open the repo with `code .` from your WSL2 terminal. All commands below run from the **repo root** in the VS Code integrated terminal (WSL2).

## Database Setup

Start the PostgreSQL container from the repo root:

```bash
docker compose up postgres -d
```

This starts Postgres on port `5432` with database `wordsets`, username `vocabversus`, password `vocabversus` — pre-configured in `Wordset.API/appsettings.Development.json`.

Apply migrations (first time, and after pulling new migrations):

```bash
dotnet ef database update --project backend/wordset-service/Wordset.Infrastructure --startup-project backend/wordset-service/Wordset.API
```


## Running

```bash
dotnet run --project backend/wordset-service/Wordset.API

# Run all tests:
dotnet test backend/wordset-service/Wordset.sln

# Run a single test:
dotnet test backend/wordset-service/Wordset.sln --filter "FullyQualifiedName~SomeTest"

# Build only:
dotnet build backend/wordset-service/Wordset.sln
```

Default port: `8080` (mapped to `5001` in docker-compose). Requires a running PostgreSQL instance — see [Database Setup](#database-setup) above.

## Debugging in VS Code

Requires the recommended extensions (see `.vscode/extensions.json`). Must be done from a VS Code window **connected to WSL2** — open the project with `code .` from your WSL2 terminal, or use the **Remote WSL: Reopen Folder in WSL** command.

1. Start the database and apply migrations — see [Database Setup](#database-setup)
2. Open the **Run and Debug** panel (`Ctrl+Shift+D`) and select **Launch: wordset-service**
3. Press `F5` — VS Code builds and launches the API with the debugger attached

## REST Endpoints

### Implemented

| Method | Path | Description | Body / Query params |
|--------|------|-------------|---------------------|
| `GET` | `/wordsets` | List wordsets, cursor-paginated | `?cursor=...&pageSize=20` |
| `POST` | `/wordsets` | Create a new wordset | `{ "name": "..." }` |
| `GET` | `/wordsets/{shareCode}` | Get wordset by share code | — |
| `PATCH` | `/wordsets/{shareCode}` | Rename a wordset | `{ "name": "..." }` |
| `GET` | `/wordsets/{shareCode}/words` | Get words, cursor-paginated | `?cursor=...&pageSize=50` |
| `PUT` | `/wordsets/{shareCode}/words` | Replace entire word list | `{ "words": [...] }` |

Full request/response schemas and parameter details are documented in the OpenAPI spec served at `/openapi/v1.json` when the service is running.

### Planned (not yet implemented)

| Method | Path | Description |
|--------|------|-------------|
| `DELETE` | `/wordsets/{shareCode}` | Delete wordset and its word partition |
| `POST` | `/wordsets/{shareCode}/validate` | Validate a word against the wordset |
| `GET` | `/wordsets/{shareCode}/letter-combinations` | All valid playable letter combinations (1–3 chars) |

## Database Schema

```sql
CREATE TABLE wordsets (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,          -- NOT unique; share_code is the unique discriminator
    share_code  CHAR(6) NOT NULL UNIQUE,-- e.g. 'XK7P2M'; generated at creation, immutable
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
