# ADR-004: Session-token player identity (localStorage UUID, no password)

**Status:** Accepted  
**Date:** 2026-06-08

## Context

The original VocabVersus generated a random UUID as a player's identity on first `CheckGame` and stored it in the in-memory `PlayerConnectionCache` keyed by SignalR `ConnectionId`. This had a documented bug (noted in a TODO comment in the original source):

> *"userID is currently error prone to multiple SignalR hub instances running concurrently, as two players could be using the same userID"*

Additionally, the `PlayerConnection → GameInstance` mapping was tied to the SignalR `ConnectionId`, which changes on every reconnect. When a player lost their connection and reconnected, their identity was essentially lost unless the server happened to still have the mapping.

For the remaster, the player identity system must:
- Survive SignalR reconnects (same browser tab refreshing or briefly going offline)
- Work correctly under multiple concurrent SignalR hub instances
- Require no registration or password from the player
- Allow the player to set a visible display name (nickname)

## Decision

Use a **client-generated UUID session token** stored in the browser's `localStorage`, combined with a nickname chosen at game-join time.

### Flow

1. **First visit**: the frontend generates a `crypto.randomUUID()` and stores it in `localStorage` as `vv_session_token`. This persists across page reloads.
2. **`CheckGame` call**: the client sends the token alongside the game ID. The server registers `token → PlayerSession` in a `ConcurrentDictionary<string, PlayerSession>` singleton (`SessionTokenCache`). The `PlayerSession` holds the token, optional nickname, and current game association.
3. **`Join` call**: the client sends token + chosen nickname. The server finds the session, sets the nickname, and adds the player to the game instance.
4. **Reconnect**: the client reconnects to SignalR (new `ConnectionId`) and calls `Reconnect` with the same token from `localStorage`. The server looks up the existing `PlayerSession` by token — identity is fully restored regardless of `ConnectionId`.
5. **Multiple hub instances**: since `SessionTokenCache` is a singleton per process, multiple hub instances on the same process share the cache. For horizontal scaling (multiple pods), a shared backing store (e.g. Redis) would be added — but this is deferred to Phase 2.

### PlayerSession model

```csharp
public record PlayerSession
{
    public string Token { get; init; }           // UUID from localStorage
    public string? Nickname { get; set; }
    public string? GameInstanceId { get; set; }  // null until player joins a game
}
```

## Consequences

**Positive:**
- Reconnects work correctly — token outlives SignalR `ConnectionId`
- No account registration flow needed for MVP
- Server-side identity is decoupled from the transient SignalR connection
- Two players cannot collide on identity: `crypto.randomUUID()` collision probability is negligible (2^122 space)

**Negative:**
- Clearing `localStorage` or switching browsers generates a new identity — the player cannot rejoin with their old nickname/score from a different device or incognito session. Acceptable for a casual game session.
- No authentication means any client that obtains another player's token can impersonate them. Acceptable for the current scope; full authentication deferred to a future phase.

## Alternatives Considered

- **Anonymous ID from server (original approach)**: Rejected — still breaks across reconnects and under multiple hub instances unless tied to a persistent store
- **Full user accounts (registration/login)**: Overkill for MVP; adds significant auth infrastructure. Can be layered on top of the session token model in the future.
- **Cookies**: Functionally equivalent to `localStorage` for this use case but slightly more complex to set cross-origin. `localStorage` is simpler for a game client.
