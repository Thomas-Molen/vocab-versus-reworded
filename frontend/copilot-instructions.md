# frontend — Copilot Instructions

## Service Overview

Vite + React 18 + TypeScript SPA. Single application covering game creation (`/`), lobby (`/lobby/:gameId`), and active gameplay (`/game/:gameId`). All real-time state arrives via SignalR — no polling.

## Build & Test

```bash
npm install
npm run dev        # dev server with HMR
npm run build      # production build
npm run preview    # serve production build
npm test           # vitest unit tests
npm test -- SomeComponent   # run a single test file
```

## SignalR Connection

The SignalR connection is owned and managed by `SignalRContext` (a React context provider). Rules:

- **Never** import or instantiate `HubConnection` directly in a component
- Consume the context via a `useSignalR()` hook
- Register event handlers (e.g. `connection.on("StartRound", ...)`) inside `useEffect` with a cleanup that calls `connection.off(...)`
- The connection is established once and shared — do not create per-component connections

## Session Token

The session token is read/written exclusively through the `useSessionToken()` hook. Never read `localStorage` directly in components or other hooks.

The token is a UUID (`crypto.randomUUID()`) generated on first load and persisted in `localStorage` as `vv_session_token`. It is sent on every SignalR invocation to identify the player server-side.

## State Management

Game state (players, scores, current round letters, timer end timestamp) lives in a context/reducer. The reducer handles all incoming SignalR events centrally — components only dispatch actions and read state.

Pattern:
```
SignalRContext                     GameStateContext
  connection.on("StartRound", ...) → dispatch({ type: "ROUND_STARTED", payload })
  connection.on("AddPoints", ...)  → dispatch({ type: "POINTS_ADDED", payload })
```

Do not store game state in component-local `useState` — it must survive navigation between lobby and game routes.

## TypeScript Conventions

- Define a TypeScript interface for every SignalR event payload, mirroring the server-side response model
- Event payload interfaces live in `src/types/signalr-events.ts`
- Use `const` enums or string literal unions for `GameState` values, matching the server enum names exactly

## Timing

Timer countdowns are derived from UTC Unix millisecond timestamps broadcast by the server (`startTimestamp`, `endTimestamp`). Use `Date.now()` on the client to calculate the remaining duration — never use `setTimeout` with a server-provided relative delay.

## Environment Variables

All environment variables are prefixed `VITE_` and accessed via `import.meta.env`:

| Variable | Description |
|----------|-------------|
| `VITE_GAME_ENGINE_URL` | Base URL for game-engine (SignalR + REST) |
| `VITE_WORDSET_SERVICE_URL` | Base URL for wordset-service REST API |

Set them in `.env.local` for local development (this file is gitignored).

## README

Keep `README.md` in this directory up to date when:
- Adding or changing routes (update the routes table)
- Adding new environment variables (update the environment variables table)
- Changing the session token key name in `localStorage`
- Changing the dev server port or proxy configuration
