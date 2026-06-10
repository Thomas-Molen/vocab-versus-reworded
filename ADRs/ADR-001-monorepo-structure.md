# ADR-001: Monorepo with per-service Docker containers

**Status:** Accepted  
**Date:** 2026-06-08

## Context

The original VocabVersus was split across 5 GitHub repositories managed via git submodules in an umbrella repo. This created friction during development: running all services required cloning and initialising submodules, cross-service contract changes required PRs in multiple repos, and the submodule commit pinning often drifted out of sync.

The new codebase is a greenfield project ("Reworded") and should optimise for developer velocity during the early agile phase while keeping services independently deployable.

## Decision

Use a single **monorepo** (`vocab-versus-reworded`) containing all services as first-class folders:

```
vocab-versus-reworded/
├── backend/
│   ├── game-engine/
│   └── wordset-service/
├── frontend/
├── ADRs/
└── docker-compose.yml
```

Each service is independently containerised with its own `Dockerfile`. A `docker-compose.yml` at the root orchestrates the full stack for local development.

## Consequences

**Positive:**
- Single `git clone` to get the full stack running
- Cross-cutting changes (e.g., shared contract types, SignalR event names) are atomic commits
- CI/CD can use path filters to only build changed services
- Easier onboarding: one repo, one `docker compose up`

**Negative:**
- A single large repo can slow down `git status`/`git log` over time if many large binary assets are committed — mitigated by keeping generated assets and build outputs in `.gitignore`
- All contributors have read access to all services by default — acceptable for this project scale

## Alternatives Considered

- **Multi-repo (original approach)**: Rejected due to submodule complexity and multi-PR overhead for contract changes
- **Polyrepo with a shared package registry**: Overkill for the current team size and phase
