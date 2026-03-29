# MDTProPlugin server — additive backend notes

This doc is for **additive** changes to the in-game C# HTTP/WebSocket server (`MDTProPlugin`). Goal: extend behavior **without** breaking the existing browser MDT UI or native clients that already depend on current URLs and message shapes.

## Request routing (where changes land)

`MDTProPlugin/MDTPro/Server.cs` dispatches:

| Prefix    | Handler            | Source file |
|-----------|--------------------|-------------|
| `/ws`     | WebSocket upgrade  | `MDTProPlugin/MDTPro/ServerAPI/WebSocketHandler.cs` |
| `/data/`  | GET-style JSON API | `MDTProPlugin/MDTPro/ServerAPI/DataAPIResponse.cs` |
| `/post/`  | POST actions       | `MDTProPlugin/MDTPro/ServerAPI/PostAPIResponse.cs` |

New **read** endpoints usually extend `DataAPIResponse` (path after `/data/`). New **mutations** usually extend `PostAPIResponse` (path after `/post/`). Keep existing paths and response bodies stable; add **new** paths or optional fields rather than renaming or repurposing old ones.

## WebSocket: envelope and commands

Clients connect to **`/ws`**. Outbound frames use JSON shaped like:

`{ "response": <payload>, "request": "<client command>" }`

(`SendData` in `WebSocketHandler` builds this.)

**Existing patterns:**

- **Interval polling over one socket:** client sends `interval/<name>`; server loops and only sends when serialized data changes (`playerLocation`, `time`, `playerCoords`).
- **Explicit commands:** e.g. `ping`, `alprSubscribe`, `shiftHistoryUpdated`, `calloutEvent` — each maps to a `switch` case.

For new features, prefer **new command strings** and **new** `interval/...` names so older clients never hit unknown behavior by accident.

## Broadcast pattern (push without polling)

`BroadcastALPRHit` is the reference implementation:

1. Maintain a **dedicated subscriber set** (e.g. `AlprSubscribers`) guarded by the same `WebSocketLock` as the master socket list.
2. On subscribe command, add the socket to that set; remove it in `finally` on disconnect.
3. When game logic fires, serialize once and `SendAsync` to each open subscriber (copy the collection under lock to avoid mutation during send).

Reuse this pattern for other server-pushed events instead of tightening global broadcast to every connection.

## Web UI compatibility

- Do not change the **meaning** of existing `/data/...` or `/post/...` responses without a versioned path or feature flag.
- WebSocket: keep the **`response` / `request`** envelope; add new `request` values clients can ignore if they do not implement them.
- **Unknown commands** already get a quoted error string; avoid turning that into breaking structured errors for commands the web UI still sends.

## WebSocket push vs HTTP polling / `interval/`

| Approach | When it fits |
|----------|----------------|
| **`interval/...`** | Frequent state that may be polled anyway; diff detection avoids spam; simple for clients that already loop. |
| **Subscribe + broadcast** | Rare or event-driven updates (ALPR-style); avoids fixed timer load; needs explicit subscribe/unsubscribe lifecycle. |
| **`/data/` or `/post/`** | One-shot reads, user actions, or clients that do not use WebSockets. |

Native desktop code can mix: HTTP for actions, WebSocket for live updates, matching what the browser MDT can adopt incrementally.
