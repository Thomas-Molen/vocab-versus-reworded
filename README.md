# vocab-versus-reworded

A remaster of the VocabVersus multiplayer vocabulary game, built from the ground up.

## Quick Start

```bash
docker compose up
```

Then open [http://localhost:3000](http://localhost:3000).

## Services

| Service | Local port | Description |
|---------|-----------|-------------|
| frontend | 3000 | Vite + React 18 SPA |
| game-engine | 5000 | .NET 10 SignalR hub + REST |
| wordset-service | 5001 | .NET 10 wordlist REST API |
| postgres | 5432 | PostgreSQL 16 |

## Architecture

See [`ADRs/`](./ADRs/) for all architectural decisions.

## Development

See [`.github/copilot-instructions.md`](./.github/copilot-instructions.md) for build commands, conventions, and architecture details.