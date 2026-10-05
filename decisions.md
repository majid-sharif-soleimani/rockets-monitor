# Decision Log — Rockets Monitor

This log records the main design decisions for the Rockets Monitor service, the alternatives we considered, and the trade-offs we accepted. Each entry gives the context, the decision, the alternatives, and the consequences.

All entries were agreed during design on 2026-10-04, before implementation. Later changes are added as new entries, not edits.

---

## DEC-01 — ASP.NET Core on .NET 10 with controllers

**Context.** The challenge allows any language. The service needs an HTTP endpoint for incoming messages and a REST API for queries.

**Decision.** ASP.NET Core on **.NET 10** (current LTS), using **controllers** and the built-in dependency injection container.

**Alternatives.** Minimal APIs: less code, but controllers give clearer grouping of endpoints by concern (single rocket vs. fleet) and familiar conventions for reviewers.

**Consequences.** A mainstream, well-supported stack. Every component is registered in one place (the Api host) and can be replaced through DI.

---

## DEC-02 — One project per purpose, domain-driven layering

**Decision.** Five projects with dependencies pointing inward:

```
Domain ← Application ← {Infrastructure, Listener.Http} ← Api
```

- **Domain:** messages, rocket state, rocket monitor, registry. No framework dependencies.
- **Application:** abstractions (`IMessageChannel`, `IMessageListener`), the consumer, query and report services, DTOs.
- **Infrastructure:** the in-memory channel.
- **Listener.Http:** receiving messages over HTTP.
- **Api:** the composition root and query controllers.

Each project has its own test project.

**Consequences.** More projects than a small service strictly needs. In return, the domain rules can be tested without any host, and the transport or queue can be swapped without touching the domain.

---

## DEC-03 — Receiving messages sits behind `IMessageListener`, on its own port

**Context.** Messages arrive over HTTP today, but other transports (Kafka, gRPC, a message broker) are realistic later.

**Decision.** `IMessageListener` (start/stop) with a default **`HttpMessageListener`** that runs its **own Kestrel server on port 8088**. The query API and Swagger run on **port 8080**. Both run in the same process and share the message channel.

**Alternatives.**
- One app serving both ports, with host-based routing. Simpler, but receiving and querying would share middleware, rate limiting and failure modes.
- Two separate processes. Clean separation, but they would need an external queue to share messages.

**Consequences.** Rate limiting and backpressure affect only message receiving; dashboard queries are never throttled by message bursts. The two ports could become two processes later, once the channel is external (see DEC-04).

---

## DEC-04 — Producer/consumer through `IMessageChannel`

**Decision.** The listener writes to `IMessageChannel` and a background consumer reads from it. The default implementation is a **bounded `System.Threading.Channels` channel** with `FullMode = Wait`. The capacity is configurable.

**Alternatives.**
- Apply each message directly in the HTTP request. No queue, but processing time would add to response time, and there would be no place to absorb bursts.
- `DropOldest` / `DropNewest` full modes. Rejected: they silently lose messages that have already been acknowledged.

**Consequences.** Receiving and processing are decoupled, and bursts are absorbed up to the channel's capacity. Kafka or another durable log can replace the in-memory channel by implementing the same interface.

---

## DEC-05 — Backpressure: 429 for rate limiting, 503 for a full channel, both with `Retry-After`

**Context.** We don't know how fast the senders are. By default the test program sends with **concurrency 3 and no delay**. The sender redelivers every message that doesn't get a 2xx response.

**Decision.**
- A **global token-bucket rate limiter** (`Microsoft.AspNetCore.RateLimiting`) on the receiving endpoint returns **429** with `Retry-After`.
- If the channel is still full after a short wait (`WriteTimeout`), the listener throws `MessageChannelFullException`, which is mapped to **503** with `Retry-After`.
- Limits and capacity are configurable. Defaults are tuned against a real run of the test program.

**Alternatives.** A per-client (IP) limit was considered. A global limit fits better, because the thing being protected is our own processing capacity, not fairness between senders.

**Consequences.** Rejected messages are redelivered by the sender, so backpressure never loses data. A single noisy sender can use up the whole global budget (accepted, see Limitations).

---

## DEC-06 — Return 202 only after the message is in the channel; unknown types get 202

**Decision.**
- **202 Accepted** is returned only after the message has been written to the channel.
- Malformed requests get **400**: invalid JSON, missing metadata, `messageNumber < 1`, or missing payload fields.
- A valid envelope with an **unknown `messageType`** gets **202** and a warning in the log, and is not processed.

**Rationale.** A non-2xx response causes the sender to redeliver. Answering an unknown type with 4xx would make the sender retry a message we will never understand, forever.

**Consequences.** If new message types are added, nothing breaks; they show up in the logs instead.

---

## DEC-07 — Rocket state is calculated without waiting for missing messages

**Context.** Messages arrive out of order. The first design held early messages in a sorted buffer and applied them only once the gap before them was filled.

**Decision.** A missing message must **never block** the state calculation. There is no waiting and no timeout. Every rule is designed so the result **does not depend on arrival order**:

| Field | Rule |
|---|---|
| Speed | `launchSpeed + Σ increases − Σ decreases`. Addition doesn't depend on order. No clamping, so speed may be temporarily negative. |
| Mission | The mission from the message with the **highest `messageNumber`** wins (launch or mission change). |
| Type / launch speed | Taken from `RocketLaunched` whenever it arrives. |
| Exploded | Once an explosion is received, it never reverts. |

Order is decided only by `messageNumber`, never by `messageTime`, because clocks can be skewed.

**Rejected alternative.** A sorted buffer that waits for gaps. One lost or delayed message would freeze a rocket's state indefinitely.

**Consequences.**
- O(1) update per message, and no recalculation: the latest state is kept as an immutable snapshot.
- No business rule may depend on order, for example "speed cannot drop below zero" or "ignore speed changes after an explosion". All messages from the source are trusted.

---

## DEC-08 — Duplicate detection with a watermark plus a sorted set; messages are not stored

**Context.** Delivery is at-least-once. Most messages are harmless when applied twice, but **speed changes are not**: a duplicated `+3000` corrupts the speed permanently.

**Decision.** Each rocket monitor stores:
- a **watermark** N, meaning every message 1..N has been applied;
- a **`SortedSet<long>`** of applied message numbers above N.

A message is a duplicate if its number ≤ N or is in the set. When a gap fills, the watermark moves forward and those numbers leave the set.

**Alternatives.**
- A `HashSet` of every applied number. Simplest, but memory grows with the number of messages.
- **Storing the messages** (a sorted log of messages per rocket). This would allow rebuilding the state by replaying them. Rejected for now to keep the design simple (see Limitations).

**Consequences.** Memory per rocket stays small once gaps fill. Messages aren't kept, so state cannot be rebuilt by replay, and a redelivery that differs in content cannot be detected.

---

## DEC-09 — First received wins for `RocketLaunched` and `RocketExploded`

**Context.** These messages are sent once. A redelivery has the same number and is caught as a duplicate. A *second, different* launch or explosion would be a sender bug.

**Decision.** The **first one received wins**. A later one with a different number is logged as a warning and ignored. For two messages with the **same number but different content**, the first received also wins; it counts as a duplicate.

**Alternative.** Lowest `messageNumber` wins. This gives the same result whatever the arrival order, at the cost of one more stored number. Rejected in favour of simplicity, since the README says it cannot happen.

**Consequences.** If a sender misbehaves, these fields could depend on arrival order. The warning in the log makes it visible.

---

## DEC-10 — One consumer, single writer per rocket, readers never lock

**Decision.**
- **One** background consumer reads the channel and applies each message to its rocket's monitor.
- Because only that consumer writes, monitors need no locks. Each one publishes an immutable `RocketState` snapshot by replacing a reference in one atomic step, so REST readers never lock.
- The registry is a `ConcurrentDictionary<string, IRocketMonitor>`.
- On shutdown, the listener stops first, then the channel is completed, then the consumer applies whatever is still in it.

**Scaling path.** N channel/consumer pairs, with messages assigned by `hash(channel)`, the same model as Kafka partitions. Each rocket's messages still go to exactly one consumer, so the single-writer rule holds.

**Consequences.** Simple and correct concurrency. Throughput is bounded by one consumer thread, which is far above what the test program produces.

---

## DEC-11 — Rocket and fleet queries are separate; responses contain only user-facing data

**Decision.**
- `RocketsController` / `IRocketQueryService`: `GET /api/rockets/{channel}`.
- `FleetController` / `IFleetReportService`:
  - `GET /api/fleet/rockets`: sort by `channel | type | speed | mission | status | launchedAt | lastUpdatedAt`, asc/desc, filter by status, paging.
  - `GET /api/fleet/summary`: counts by status, type and mission.
- Rockets whose launch hasn't arrived yet are listed with **`launched: false`** and sort last.
- Responses contain **only what a dashboard user cares about**. Internal state (watermarks, gaps, pending counts) is never exposed.
- Controllers return DTOs, never domain objects. Sorting is done on each read from the snapshots.

**Consequences.** A clear split by concern, and API contracts that are independent of the domain's internals. Sorting on read costs O(n log n) per request, which is fine for thousands of rockets.

---

## DEC-12 — Logging: Serilog, console only

**Decision.** Serilog behind the standard `ILogger<T>`, writing to the **console only**. A rolling log file was planned first and then dropped. The default level is Information; per-message detail is logged at Debug and switched on through configuration.

**Open point.** With console-only output, ASP.NET's built-in console logger would be enough. Serilog is kept for now because it was chosen explicitly. Swapping it is a one-line change in the host, because the code only uses `ILogger<T>`.

---

## DEC-13 — Testing: xUnit with a TDD approach, focused on the core behaviour

**Decision.** xUnit, tests written before the code. Coverage focuses on:
- the domain rules, including an **order-independence test**: the same messages in several shuffled orders, with duplicates mixed in, must give the same state;
- the channel's backpressure;
- the listener's status codes (202/400/429/503);
- the query API, through `WebApplicationFactory`.

There is also an end-to-end run against the real `rockets` program with its default settings. We don't aim for exhaustive coverage.

---

*The entries below were added during implementation, on 2026-10-04.*

## DEC-14 — Default rate limit raised to 20,000 per second, based on measurement

**Context.** The first default was 2,000 per second. A full default run of the test program against it produced about 379,000 `429` responses. The run also revealed how the program behaves:
- It retries rejected messages every **500 ms and ignores `Retry-After`**.
- When it finishes producing, it **drops whatever is still waiting for redelivery**: 4,516 messages were lost ("Redelivering message failed: writer closed").
- The resulting state was wrong for 15 of the 20 rockets.

With the limit effectively off, the program peaks at **about 11,000 messages per second**.

**Decision.** The default is 20,000 per second, about twice the observed peak. Rate limiting stays in place against floods, but the expected load is never throttled. The limit can be changed in configuration.

**Consequences.** A 429 is safe only if the sender really does redeliver. Against this program, a limit set too low causes data loss. In general, the limit should be sized from measured load, not guessed.

---

## DEC-15 — The consumer is a plain `IHostedService`, not a `BackgroundService`

**Context.** A consumer test failed intermittently, and then consistently. In .NET 10, `BackgroundService` starts `ExecuteAsync` through `Task.Run` with the stopping token. If the host stops before that work is scheduled, the loop never runs, and `StopAsync` returns without processing the channel. Messages that had already been acknowledged were never applied.

**Decision.** `RocketMessageConsumer` implements `IHostedService` directly:
- `StartAsync` starts the read loop with no cancellation token.
- `StopAsync` completes the channel and waits for the loop to finish draining it, bounded by the host's shutdown timeout.

**Consequences.** Draining on shutdown is explicit and no longer depends on `BackgroundService` internals. The test was run 20 times in a row without a failure.

---

## DEC-16 — The listener's embedded web app does not own the process lifetime

**Context.** `HttpMessageListener` runs its own `WebApplication` (DEC-03). By default that app also registers a console lifetime, so it reacted to Ctrl+C/SIGTERM on its own. The log showed the shutdown happening twice, and the shutdown order was no longer controlled by the main host.

**Decision.** The listener's app uses a no-op `IHostLifetime`. It only starts and stops when the main host tells it to, which keeps the order from DEC-10: the listener stops first, then the channel is drained.

---

*The entries below were added on 2026-10-05, after the first implementation.*

## DEC-17 — Implementation classes are internal; each project exposes interfaces and a registration method

**Context.** In the first implementation most classes were `public`, including ones that no other project needs to name.

**Decision.**
- **Domain:** `RocketMonitor` is `internal`. Other projects use it only through `IRocketMonitor`, and get instances from `IRocketRegistry`. `MessageNumberTracker` (the watermark and sorted set from DEC-08) is a private class inside `RocketMonitor`.
- **Application:** `RocketMessageConsumer` and `MessageListenersHostedService` are `internal`. A public `ServiceRegistry.AddApplicationServices()` registers them, together with the two report services, and `Program.cs` calls it.
- The registration order of the two hosted services, which decides the shutdown order (DEC-10), now lives inside `AddApplicationServices()` instead of in `Program.cs`.
- Test projects reach internal classes through `InternalsVisibleTo`.

**Consequences.**
- The public surface of each project is its interfaces, its data types and one registration method.
- The host can no longer register the two hosted services in the wrong order.
- `MessageNumberTracker` can no longer be tested directly. Duplicate detection is tested through `RocketMonitor`.

**Not changed.** `RocketRegistry`, the two report services, `InMemoryMessageChannel` and `HttpMessageListener` are still `public` and are registered from `Program.cs` or `AddApplicationServices()`.

---

## DEC-18 — The end-to-end verification scripts are not kept in the repository

**Context.** The end-to-end check (see Verification) used two one-off Python scripts: one recorded the test program's messages, the other calculated the expected state independently and compared it with the API. They were first committed under `tools/e2e`.

**Decision.** The scripts were removed. The repository contains only the service and its tests.

**Consequences.** The result of the check is recorded below, but it cannot be rerun from the repository. The scripts remain in git history (commit `a33f0a9`).

---

## Verification

*Measured on 2026-10-04, before DEC-17 and DEC-18.*

| Check | Result |
|---|---|
| Unit and integration tests (`dotnet test`) | 55 passing |
| Default run of the test program (100,000 messages, concurrency 3, no delay) | All accepted in ~16 s, no 429 or 503, no warnings or errors in the log |
| State compared with an independent, throwaway Python implementation of the rules (not kept in the repository) | **0 mismatches** across all 20 rockets |
| Graceful shutdown (SIGINT) | One clean shutdown, the channel drained |
| Swagger | Each port documents only its own endpoints |

The independent check works because the program's messages are deterministic for a given seed. Two capture runs contained identical messages.

**Open item (2026-10-05).** After DEC-17, `tests/Rockets.Domain.Tests/MessageNumberTrackerTests.cs` still refers to `MessageNumberTracker` directly, so the domain test project does not compile. The file has to be removed, or its three cases moved into `RocketMonitorTests`, before the test count above holds again.

**Coverage gap.** With the default settings the program sent no duplicates (they only appear on redelivery) and only a few messages arrived out of order. So the end-to-end run barely exercises duplicate detection and reordering. Those cases are covered by the domain tests, in particular the order-independence test with shuffled and duplicated messages.

---

## Accepted limitations

| Limitation | Why accepted | Way forward |
|---|---|---|
| State is in memory only and lost on restart. The sender won't redeliver messages that already got a 202. | Keeps the challenge within scope. | A durable channel or event store, returning 202 only after the durable write. |
| The state cannot be rebuilt, because messages aren't stored. | Follows from DEC-08. | An event store behind an interface, with replay (the order-independent rules make replay straightforward). |
| A second launch or explosion: first received wins. | The README says it cannot happen. | Lowest-number-wins (DEC-09 alternative). |
| A redelivery with different content can't be detected. | Messages aren't stored. | Store message hashes or full messages. |
| One consumer, one process. | More than enough for the test load. | Split by channel hash (DEC-10), external channel (DEC-04). |
| The rate limit is global. | Protects our own capacity, which is the actual concern. | A per-partition or per-client limit. |
| Sorting is done on every read. | Fine at thousands of rockets. | Sorted indexes or a cached, periodically rebuilt view. |
