# Native MDT collaboration roadmap

This document outlines phased work toward **real-time, multi-user** behavior in the native MDT. The existing **web MDT remains the primary product**; native and server work should **extend** the platform. **Plugin and API changes should be additive** (new capabilities, optional paths, backward-compatible contracts) so web and native can share backends without breaking current deployments.

---

## Phase 1: Server push for all entities

**Goal:** Clients receive authoritative updates without polling for every entity type the MDT cares about (units, calls, persons, vehicles, BOLOs, notes, etc., as applicable).

**Direction:**

- Define a small set of **subscription topics** or **resource scopes** aligned with how the native UI loads data.
- Use a single **real-time channel** (e.g. WebSocket or SSE) with **versioned snapshots** plus **incremental events**, so reconnect and cold start stay predictable.
- Specify **ordering and idempotency** (event IDs, sequence per aggregate) so duplicate or out-of-order delivery does not corrupt local state.

**Exit criteria:** Opening two clients against the same environment shows the same entity graph updating in near real time after server-side changes, without manual refresh.

---

## Phase 2: Authentication and TLS

**Goal:** Multi-user access is **identity-bound**, **encrypted in transit**, and ready for **role-based** rules on the server.

**Direction:**

- **TLS** for all client–server traffic in production; document dev exceptions if any.
- **Auth** integrated with your existing identity story (tokens, refresh, session expiry) so push connections authenticate the same way as REST/RPC.
- **Authorization** on subscribe and on each pushed event class (users only receive data their role allows).

**Exit criteria:** Anonymous or wrong-role clients cannot subscribe to sensitive feeds; traffic is not cleartext across untrusted networks.

---

## Phase 3: Co-editing reports — CRDT vs field locks

**Goal:** Multiple officers can work on **reports** (or long-form records) without silent overwrites.

**Options:**

| Approach | Pros | Cons |
|----------|------|------|
| **Field / section locks** | Simple to reason about; minimal new infra | Coarse; can block; needs timeout/steal rules |
| **CRDT or OT** | Fine-grained merge; better concurrent editing | Heavier model; needs careful schema and testing |

**Recommendation:** Start with **explicit locks** (or optimistic concurrency with **ETags / revision** per report) for a fast path; evaluate **CRDT** only where true simultaneous paragraph-level editing is required, and only for bounded document types.

**Exit criteria:** Documented conflict policy; no last-write-wins without user visibility; optional path for stricter merge later.

---

## Phase 4: SQLite concurrency

**Goal:** The native client’s **local SQLite** stays correct under **async server updates** and **local edits**, including offline or flaky networks.

**Direction:**

- Single-writer discipline (e.g. one connection or serialized queue) for mutations; **readers** can use WAL where appropriate.
- Clear **layers:** “server truth” vs “pending local changes” vs “merged view” so push handlers do not fight the UI thread.
- **Migration strategy** for schema changes when push adds new fields or entity types.

**Exit criteria:** Stress tests with concurrent push and local saves do not deadlock or corrupt the DB; recovery after crash is defined.

---

## Cross-cutting notes

- **Web MDT stays:** Native collaboration features must not require removing or replacing the web app; shared backends should serve both.
- **Additive plugins:** Prefer new hooks, optional manifest fields, and versioned APIs over breaking changes to existing plugin contracts.

---

## Summary order

1. Server push for all entities  
2. Auth and TLS on the real-time path  
3. Report co-editing (locks/revisions first; CRDT where justified)  
4. SQLite concurrency and consistency with server events  

This order reduces the risk of building merge or locking logic on top of stale or polling-based data.
