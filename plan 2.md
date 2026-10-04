# Plan 2 — Rockets Monitor

Status: **draft for review**. Nothing is implemented until approved.

## 1. Goal

A .NET service that takes in rocket messages from the `rockets` test program and exposes each rocket's state through a REST API that a dashboard can use. It must handle out-of-order delivery, duplicates (at-least-once delivery) and bursts of traffic. The test program's defaults are **concurrency 3, no delay between messages, 100,000 messages**.

## 2. Agreed decisions

| # | Decision |
|---|---|
| D1 | ASP.NET Core on **.NET 10** (SDK 10.0.112 installed), with **controllers** and the built-in DI. |
| D2 | Message receiving sits behind **`IMessageListener`**. The default `HttpMessageListener` runs its **own Kestrel server on port 8088**. The query API and Swagger run on **port 8080**. |
| D3 | Producer/consumer through **`IMessageChannel`**. The default is a bounded `System.Threading.Channels` channel; Kafka or similar can replace it later. |
| D4 | **429** from a **global** rate limiter and **503** when the channel is full, both with `Retry-After`. Channel capacity is configurable. |
| D5 | **`202 Accepted`** is returned only after the message is written to the channel. A valid envelope with an **unknown `messageType`** also gets `202` and a warning in the log. Malformed requests get `400`. |
| D6 | State is calculated **without waiting for missing messages**. Every rule gives the same result whatever order messages arrive in. |
| D7 | Duplicates are detected with a **watermark plus a sorted set** of applied message numbers. Messages are not stored. |
| D8 | `RocketLaunched` / `RocketExploded`: the **first one received wins**. A later one with a different number is logged as a warning and ignored. |
| D9 | Rockets that haven't launched yet are **shown with `launched: false`**. |
| D10 | Reports show only **user-facing** data, with no internal or technical state. |
| D11 | **One consumer** now. The design allows splitting by `hash(channel)` across N consumers later. |
| D12 | **Serilog**, writing to the **console only**, behind the standard `ILogger<T>`. |
| D13 | **xUnit** with a TDD approach. Tests cover the core behaviour only. |
| D14 | **Swagger UI** (Swashbuckle) and a **git** repository. |

## 3. Architecture

```
 rockets (test program)
        │ POST /messages  (port 8088)
        ▼
┌──────────────────────┐   TryWrite   ┌──────────────────┐   ReadAll   ┌──────────────────────┐
│ HttpMessageListener  │ ───────────▶ │  IMessageChannel │ ──────────▶ │ RocketMessageConsumer│
│ (IMessageListener)   │  202/429/503 │ (bounded, in-mem)│             │   (BackgroundService)│
└──────────────────────┘              └──────────────────┘             └──────────┬───────────┘
                                                                                  │ Apply
                                                                                  ▼
                                                     ┌────────────────────────────────────────┐
                                                     │ IRocketRegistry                        │
                                                     │   channel → IRocketMonitor             │
                                                     │             (latest RocketState snapshot)│
                                                     └───────────────────┬────────────────────┘
                                                                         │ read snapshots (no locks)
                                                  ┌──────────────────────┴───────────────────┐
                                                  ▼                                          ▼
                                       IRocketQueryService                         IFleetReportService
                                       RocketsController                           FleetController
                                       GET /api/rockets/{channel}                  GET /api/fleet/rockets
                                                                                   GET /api/fleet/summary
                                                  (port 8080, Swagger UI)
```

### Projects (one per purpose)

```
RocketsMonitor.slnx
Directory.Build.props              – net10.0, nullable, implicit usings, warnings as errors
src/
  Rockets.Domain                   – messages, RocketState, RocketMonitor, RocketRegistry + interfaces. No dependencies.
  Rockets.Application              – IMessageChannel, IMessageListener, RocketMessageConsumer,
                                     IRocketQueryService, IFleetReportService, DTOs, query options
  Rockets.Infrastructure           – InMemoryMessageChannel (System.Threading.Channels), options
  Rockets.Listener.Http            – HttpMessageListener (own Kestrel host on 8088), ingestion controller,
                                     envelope parsing → domain message, rate limiter, 503 mapping
  Rockets.Api                      – composition root: Program.cs, DI, query controllers, Swagger, Serilog
tests/
  Rockets.Domain.Tests
  Rockets.Application.Tests
  Rockets.Infrastructure.Tests
  Rockets.Listener.Http.Tests
  Rockets.Api.IntegrationTests
decisions.md                       – decision log (trade-offs, accepted limitations), in the repository root
docs/
  ai-usage.md                      – how AI was used, what was verified/overridden
```

Dependency direction: `Domain ← Application ← {Infrastructure, Listener.Http} ← Api`.

## 4. Domain (`Rockets.Domain`)

### Messages
`RocketMessage` is an abstract record with `Channel`, `MessageNumber` (long) and `MessageTime` (DateTimeOffset). Its subtypes are:
`RocketLaunched(Type, LaunchSpeed, Mission)`, `RocketSpeedIncreased(By)`, `RocketSpeedDecreased(By)`,
`RocketExploded(Reason)`, `RocketMissionChanged(NewMission)`.

### `RocketState` (immutable snapshot, user-facing)
`Channel`, `Launched`, `Type?`, `Speed`, `Mission?`, `Status` (`NotLaunched | Active | Exploded`),
`ExplosionReason?`, `LaunchedAt?` (launch message time), `LastUpdatedAt` (latest message time seen).

### `IRocketMonitor` / `RocketMonitor`, one per rocket
- `bool Apply(RocketMessage)` returns `false` for duplicates.
- `RocketState Current` holds the latest snapshot. It is replaced in one atomic step after each applied message, so readers never lock.
- **Single writer:** only the consumer calls `Apply`, so no locks are needed.

**Duplicate check (D7):** `watermark` = the highest N such that messages 1..N have all been applied, plus a `SortedSet<long>` of applied numbers above N.
- If `number <= watermark` or the number is in the set, it's a duplicate: ignore it.
- Otherwise add it to the set, then move the watermark forward while `watermark + 1` is in the set, removing those numbers as it goes. Memory stays small once gaps fill.

**State rules (D6, D8), each independent of arrival order:**

| Message | Rule |
|---|---|
| `RocketLaunched` | The first one received sets `Type`, `LaunchSpeed`, `LaunchedAt` and `Launched = true`. Its mission is used only if its number is higher than the current mission's message number. A later launch with a different number triggers a warning and is ignored. |
| `RocketSpeedIncreased` / `Decreased` | `speedDelta ± by`. The reported `Speed = LaunchSpeed (0 if not launched) + speedDelta`. No clamping: it may be temporarily negative. |
| `RocketMissionChanged` | Applied only if `messageNumber > missionMessageNumber` (the highest number wins). |
| `RocketExploded` | The first one received sets `Status = Exploded` and `ExplosionReason`. It never reverts. Later explosions trigger a warning and are ignored. |
| any | `LastUpdatedAt = max(LastUpdatedAt, MessageTime)`. |

`Status` is `Exploded` if an explosion has been received, otherwise `Active` if launched, otherwise `NotLaunched`.
Ordering is always decided by `messageNumber`, never by `messageTime`.

### `IRocketRegistry` / `RocketRegistry`
A `ConcurrentDictionary<string, IRocketMonitor>`. It provides `GetOrCreate(channel)` (called by the consumer), `TryGet(channel)` and `GetAll()` (snapshots).

## 5. Ingestion (`Rockets.Listener.Http` + `Rockets.Infrastructure`)

### `IMessageListener`
`StartAsync` / `StopAsync`. `HttpMessageListener` builds and runs its own small `WebApplication` on **8088**. It shares the singleton `IMessageChannel` from the main container. A hosted service in the Api starts and stops it.

### `POST /messages` (port 8088)
1. Parse the envelope: `metadata` into a typed object, `message` as a `JsonElement`. Then convert to a domain message based on `messageType`. (System.Text.Json's built-in polymorphism can't be used, because the type field sits in `metadata`, not in the payload.)
2. Responses:
   - Malformed JSON, missing metadata, `messageNumber < 1`, or required payload fields missing → **400** with ProblemDetails.
   - Unknown `messageType` → log a warning, **202**, nothing is written to the channel.
   - Rate limit exceeded → **429** + `Retry-After` (global token bucket from `Microsoft.AspNetCore.RateLimiting`, queue limit 0).
   - Channel full → `MessageChannelFullException` → **503** + `Retry-After` (mapped by an exception filter).
   - Written to the channel → **202 Accepted**.

### `IMessageChannel` / `InMemoryMessageChannel`
- `ValueTask WriteAsync(RocketMessage, CancellationToken)`: tries to write; if the channel is full, waits up to `WriteTimeout`, then throws `MessageChannelFullException`.
- `IAsyncEnumerable<RocketMessage> ReadAllAsync(CancellationToken)`, `Complete()`, `Count` (logged only).
- Bounded channel, `FullMode = Wait` (never drop messages that were already acknowledged), `SingleReader = true`.

### Configuration (`appsettings.json`, Options pattern)
```json
"Listener":    { "Url": "http://0.0.0.0:8088", "RetryAfterSeconds": 1 },
"RateLimit":   { "TokenLimit": 2000, "TokensPerPeriod": 2000, "ReplenishmentPeriod": "00:00:01" },
"Channel":     { "Capacity": 10000, "WriteTimeout": "00:00:00.100" },
"Api":         { "Url": "http://0.0.0.0:8080" }
```
These defaults are starting values. They get tuned in step 7 so that a default `rockets launch` run finishes cleanly.

## 6. Consumer (`Rockets.Application`)

`RocketMessageConsumer : BackgroundService` reads `ReadAllAsync`, calls `registry.GetOrCreate(channel).Apply(msg)`, and logs duplicates at Debug level.
- If applying a message throws, it logs an error and **continues**. The message was already acknowledged, so the loop must not stop.
- **Shutdown order:** the listener stops first (no new 202s), then the channel is completed, then the consumer processes what's left. This is done by the order hosted services are registered.
- **Scaling later (D11):** N channels/consumers split by `hash(channel)`. One rocket's messages always go to the same consumer, so the single-writer rule still holds.

## 7. Query API (`Rockets.Application` services + `Rockets.Api` controllers, port 8080)

| Controller | Service | Endpoint | Response |
|---|---|---|---|
| `RocketsController` | `IRocketQueryService` | `GET /api/rockets/{channel}` | `200 RocketDto` / `404` |
| `FleetController` | `IFleetReportService` | `GET /api/fleet/rockets?sortBy=&order=&status=&page=&pageSize=` | `200 PagedResult<RocketDto>` / `400` |
| `FleetController` | `IFleetReportService` | `GET /api/fleet/summary` | `200 FleetSummaryDto` |
| — | — | `GET /health` (both ports) | `200` |

- **`RocketDto`:** channel, launched, type, speed, mission, status, explosionReason, launchedAt, lastUpdatedAt.
- **Sorting:** `sortBy` = `channel | type | speed | mission | status | launchedAt | lastUpdatedAt`, with `order` = `asc | desc`. The default is `channel asc`. Null values (not launched) always sort last. Ties are broken by `channel` so paging is stable.
- **Filter:** `status` = `notLaunched | active | exploded`.
- **Paging:** `page` defaults to 1. `pageSize` defaults to 50, maximum 500.
- **`FleetSummaryDto`:** total, active, exploded, notLaunched, counts by type, counts by mission.
- Domain types never leave the Application layer; controllers return DTOs only.
- Swagger UI runs at `http://localhost:8080/swagger`, with XML comments. The listener has its own Swagger at `:8088/swagger` for posting test messages by hand.

## 8. Logging

**Console only.** Serilog (`Serilog.AspNetCore`, `Serilog.Sinks.Console`), configured from `appsettings.json`. No file logging.
- The default minimum level is **Information**, so a 100k-message run doesn't flood the console. Switch to Debug in `appsettings.Development.json` or with an environment variable when the per-message detail is needed.
- Log levels:
  - Debug: each message received or applied, and duplicates.
  - Information: start/stop and new rockets seen.
  - Warning: unknown type, a second launch or explosion, 429/503 responses (sampled).
  - Error: consumer failures.
- Code uses only `ILogger<T>`; Serilog is wired in the Api host.

## 9. Testing (TDD: write the test, then the code)

**Rockets.Domain.Tests**
- A launch sets type, speed, mission and `Launched = true`.
- Speed increases and decreases are applied. Speed received before the launch is still counted once the launch arrives.
- Mission: the highest message number wins whatever the arrival order.
- An explosion sets the status permanently. A second explosion or launch is ignored.
- Duplicates (same number, before and after the watermark) are ignored and don't change speed.
- **Order independence:** a fixed message set applied in several shuffled orders, with duplicates mixed in, gives the same final state.
- Watermark: gaps filling move it forward (internal test).
- Registry: `GetOrCreate` returns the same monitor for the same channel.

**Rockets.Application.Tests**
- Fleet report: sorting by each key and direction, nulls last, status filter, paging, summary counts.
- Query service: an unknown channel returns null (→ 404).
- Consumer: messages written to a channel end up in the registry; an exception while applying doesn't stop the loop.

**Rockets.Infrastructure.Tests**
- Channel: messages are read in the order written; a full channel throws `MessageChannelFullException` after the timeout.

**Rockets.Listener.Http.Tests** (listener app on `TestServer`)
- A valid message → 202 and it's in the channel. Malformed → 400. Unknown type → 202 and nothing in the channel.
- Channel full → 503 + `Retry-After`. Rate limit exceeded → 429 + `Retry-After`.

**Rockets.Api.IntegrationTests** (`WebApplicationFactory`)
- `GET /api/rockets/{channel}` → 200/404. `GET /api/fleet/rockets` sorting and paging work, and invalid `sortBy` → 400. `GET /api/fleet/summary`.

## 10. Implementation steps (one commit each, tests before code)

0. `git init`, `.gitignore`, solution and projects, `Directory.Build.props`, empty docs.
1. Domain: messages, `RocketState`, `RocketMonitor` (duplicate check and rules), `RocketRegistry`.
2. Infrastructure: `InMemoryMessageChannel` + options.
3. Application: consumer, query service, fleet report service, DTOs.
4. Listener.Http: envelope parsing, ingestion controller, rate limiter, 503 mapping, `HttpMessageListener`.
5. Api: composition root, controllers, Swagger, Serilog, health, hosted service order.
6. Integration tests across the whole app.
7. **End-to-end check:** run `../linux_amd64/rockets launch "http://localhost:8088/messages"` with **default values**. Check that it finishes, see how many 429/503 responses occur, and tune the defaults. Compare a few rockets' state against the program's debug log (`--log-level debug`). Record the results.
8. Docs: README (build, run, test, endpoints), `decisions.md`, `ai-usage.md`.

## 11. Accepted limitations (to record in `decisions.md`)

- **State is in memory only.** It's lost on restart, and the sender won't redeliver acknowledged messages. Next step: a durable event store or channel, with 202 returned only after a durable write.
- **No rebuilding state**, because messages aren't stored. A future event store would enable replay.
- **First received wins** for launch and explosion. A misbehaving sender could make these fields depend on arrival order.
- **Same number, different content:** the first one received wins; the difference can't be detected.
- **One consumer and one process.** Splitting by channel hash is the scaling path.
- **The rate limit is global, not per client.**
- Sorting is done on each read, O(n log n), which is fine for thousands of rockets. An index or cache would be needed at a much larger scale.
