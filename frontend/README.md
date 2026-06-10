# frontend

Vite + React 18 + TypeScript SPA for VocabVersus Reworded. Single application covering game creation, lobby, and gameplay — replacing the original two separate frontends.

## Running

```bash
npm install
npm run dev       # dev server with HMR at http://localhost:5173
npm run build     # production build to dist/
npm run preview   # preview production build
npm test          # vitest unit tests
npm test -- SomeComponent  # run a single test file
```

The dev server proxies `/gamehub` to the game-engine service. Configure service URLs in `.env.local`:

```
VITE_GAME_ENGINE_URL=http://localhost:5000
VITE_WORDSET_SERVICE_URL=http://localhost:5001
```

## Routes

| Path | Component | Description |
|------|-----------|-------------|
| `/` | `HomePage` | Create a new game or browse wordlists |
| `/lobby/:gameId` | `LobbyPage` | Pre-game lobby; players join and ready up |
| `/game/:gameId` | `GamePage` | Active gameplay: letters, submission, scoreboard |

## Session Token

A UUID session token is generated on first load via `crypto.randomUUID()` and stored in `localStorage` as `vv_session_token`. Access it exclusively through the `useSessionToken()` hook — never read `localStorage` directly in components.

The token is sent with every SignalR call and survives page refreshes and reconnects. See [ADR-004](../ADRs/ADR-004-player-identity.md).

## SignalR

The SignalR connection lifecycle is managed by `SignalRContext` (a React context provider wrapping `@microsoft/signalr`). All components that need real-time data consume this context — do not instantiate `HubConnection` directly in components.

All game state arrives via SignalR push events. Do not poll REST endpoints during active gameplay.

## State Management

Game state (players, current round, scores, timer) lives in a React context/reducer updated by SignalR event handlers. The reducer handles all `StartRound`, `AddPoints`, `UserJoined`, etc. events centrally.
