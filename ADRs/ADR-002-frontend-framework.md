# ADR-002: Vite + React 18 SPA for frontend

**Status:** Accepted  
**Date:** 2026-06-08

## Context

The original VocabVersus had **two separate frontend applications**: a "dashboard" for game creation and lobby management, and an "interface" for the actual gameplay. This split caused duplication of shared UI components and required players to navigate between two separately hosted apps.

For the remaster, both concerns should live in a single frontend application with client-side routing.

The frontend is a **real-time game client**. The majority of UI state changes are driven by SignalR push events (letters revealed, score updates, timer ticks, player joins/leaves). Server-side rendering (SSR) provides no meaningful benefit here: the page content is almost entirely dynamic and requires an active WebSocket connection to be useful.

## Decision

Use **Vite + React 18 + TypeScript** as a Single Page Application (SPA).

- **Vite** as the build tool and dev server (fast HMR, minimal config)
- **React 18** with hooks for component state and side effects
- **TypeScript** for type safety on SignalR event payloads and game state
- **React Router v6** for client-side routing (`/`, `/lobby/:gameId`, `/game/:gameId`)
- **`@microsoft/signalr`** npm package for the SignalR client

## Consequences

**Positive:**
- No SSR overhead or hydration complexity — all rendering is client-driven
- Single codebase for lobby and gameplay replaces the original two-app setup
- Vite's dev server proxies SignalR WebSocket connections easily, simplifying local development
- TypeScript contracts for SignalR events can be shared across components

**Negative:**
- Initial page load requires JavaScript to be enabled and the JS bundle to load before anything renders — acceptable for a game application where players actively navigate to the game
- No SEO benefit — not relevant for a game client

## Alternatives Considered

- **Next.js**: Would add SSR/SSG capabilities, but these are unused overhead for a real-time game client. The App Router and Server Components add mental complexity with no payoff here.
- **Remix**: Same SSR-centric reasoning applies. Better suited for content/data-heavy apps.
- **Vue / Svelte**: Both viable, but React has the strongest ecosystem for `@microsoft/signalr` integration examples and the team has prior familiarity.
