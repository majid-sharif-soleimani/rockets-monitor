# Rockets Monitor

A .NET 10 service that receives rocket messages and exposes each rocket's current state through a REST API for a dashboard. The challenge description is in [docs/CHALLENGE.md](docs/CHALLENGE.md). Design decisions and trade-offs are in [decisions.md](decisions.md).

## Requirements

- .NET SDK 10.0

## Run

```bash
dotnet run --project src/Rockets.Api -c Release
```

The service listens on two ports:

| Port | Purpose | Swagger |
|---|---|---|
| **8088** | Receives messages: `POST /messages` | http://localhost:8088/swagger |
| **8080** | Query API for the dashboard | http://localhost:8080/swagger |

Then start the test program, for example from the challenge ZIP's `linux_amd64` folder:

```bash
./rockets launch "http://localhost:8088/messages"
```

## API (port 8080)

| Endpoint | Description |
|---|---|
| `GET /api/rockets/{channel}` | Current state of one rocket (`404` if unknown). |
| `GET /api/fleet/rockets` | All rockets. Query parameters: `sortBy` = `channel` (default) / `type` / `speed` / `mission` / `status` / `launchedAt` / `lastUpdatedAt`; `order` = `asc` / `desc`; `status` = `active` / `exploded` / `notLaunched`; `page` (default 1); `pageSize` (default 50, max 500). Missing values always sort last. |
| `GET /api/fleet/summary` | Counts by status, type and mission. |
| `GET /health` | Liveness, on both ports. |

Example rocket:

```json
{
  "channel": "193270a9-c9cf-404a-8f83-838e71d9ae67",
  "launched": true,
  "type": "Falcon-9",
  "speed": 3500,
  "mission": "ARTEMIS",
  "status": "active",
  "explosionReason": null,
  "launchedAt": "2022-02-02T19:39:05.86337+01:00",
  "lastUpdatedAt": "2022-02-02T19:39:07.12000+01:00"
}
```

## Receiving messages (port 8088)

| Response | When |
|---|---|
| `202 Accepted` | The message is queued, or its `messageType` is unknown (logged and ignored). |
| `400 Bad Request` | The message is malformed. |
| `429 Too Many Requests` + `Retry-After` | The global rate limit is exceeded. |
| `503 Service Unavailable` + `Retry-After` | The queue is full. |

## Configuration

Settings are in `src/Rockets.Api/appsettings.json` and can be overridden with environment variables (e.g. `RateLimit__TokenLimit=50000`).

| Section | Settings |
|---|---|
| `Urls` | Query API address (default `http://0.0.0.0:8080`). |
| `Listener` | `Url` (default `http://0.0.0.0:8088`), `RetryAfterSeconds`. |
| `RateLimit` | Global token bucket: `TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriod` (default 20,000 per second). |
| `Channel` | Queue `Capacity` (default 10,000) and `WriteTimeout` before answering 503 (default 100 ms). |
| `Serilog` | Console logging. Set `Serilog__MinimumLevel__Override__Rockets=Debug` to log every message. |

## Ubiquitous language

These terms mean the same thing in the code, the API, the tests and the documents.

One word is used for two different things, so it is always qualified: a rocket's **channel** is its radio channel (its identity), while the **message channel** is the internal queue.

### Rockets and their messages

| Term | Meaning |
|---|---|
| **Rocket** | The thing being monitored. The service knows a rocket only through the messages it sends. |
| **Channel** | A rocket's radio channel. Each rocket has its own, so the channel is the rocket's ID (`GET /api/rockets/{channel}`). |
| **Message** | One state change reported by a rocket: `RocketLaunched`, `RocketSpeedIncreased`, `RocketSpeedDecreased`, `RocketMissionChanged` or `RocketExploded`. |
| **Message number** | The message's position within its channel, starting at 1. It is the only thing that decides order. |
| **Message time** | When the rocket sent the message. It is shown to users but never used to order messages. |
| **Envelope** | A message as it arrives over HTTP: the `metadata` (channel, number, time, type) plus the `message` payload. |
| **Launched** | The rocket's `RocketLaunched` message has been received. Before that, the rocket can already exist with `launched: false`, because other messages arrived first. |
| **Speed** | The launch speed plus all increases, minus all decreases. |
| **Mission** | The rocket's current mission: the one from the message with the highest message number. |
| **Status** | `notLaunched`, `active` or `exploded`. An exploded rocket stays exploded. |
| **Rocket state** | Everything currently known about one rocket: launched, type, speed, mission, status, explosion reason and times. It is an immutable snapshot. |
| **Fleet** | All rockets the service has seen. |

### Delivery

| Term | Meaning |
|---|---|
| **At-least-once delivery** | Every message arrives one or more times. Nothing is skipped on purpose, but the same message can come again. |
| **Redelivery** | The sender sends a message again because it did not get a 2xx response. |
| **Duplicate** | A message whose channel and message number were already applied. It is ignored. |
| **Out of order** | A message arrives before one with a lower message number. |
| **Gap** | A message number that has not arrived yet while higher ones have. The service never waits for a gap to fill. |
| **Accept (acknowledge)** | Answer `202`. It means the message is safely in the message channel, so the sender must not send it again. |
| **Apply** | Use a message to update its rocket's state. A message is applied once, whatever order it arrives in. |
| **Watermark** | For one rocket, the highest number N such that every message from 1 to N has been applied. Used to detect duplicates. |

### Parts of the service

| Term | Meaning |
|---|---|
| **Listener** | Receives messages from a transport and writes them to the message channel. The default one is an HTTP server on port 8088. It is the producer. |
| **Message channel** | The queue between listeners and the consumer. It has a fixed capacity. |
| **Consumer** | Reads the message channel and applies each message to the right rocket monitor. There is one consumer. |
| **Rocket monitor** | Keeps one rocket's state up to date. There is one monitor per rocket, and it holds the rules for applying messages. |
| **Registry** | Keeps track of every rocket monitor, and creates one the first time a channel is seen. |
| **Rocket report** | The current state of a single rocket. |
| **Fleet report** | A view of all rockets: the sorted, filtered and paged list, or the summary. |
| **Fleet summary** | Counts of rockets by status, type and mission. |
| **Backpressure** | What happens when messages arrive faster than they can be processed: the message channel fills up, and the listener answers `503` until there is room. |
| **Rate limit** | A cap on how many messages per second the listener accepts, shared by all senders. Above it the listener answers `429`. |

## Project structure

The solution has five source projects, one per purpose, and one test project for each. Dependencies point inward: `Domain ← Application ← {Infrastructure, Listener.Http} ← Api`.

Message flow: `HttpMessageListener` → `IMessageChannel` → `RocketMessageConsumer` → `RocketMonitor` (one per rocket) → immutable `RocketState` snapshots → query services → controllers.

### `src/Rockets.Domain`

The business rules. It has no dependencies on ASP.NET or any other framework.

- **`Messages/RocketMessage.cs`**: the five message types as records (`RocketLaunched`, `RocketSpeedIncreased`, `RocketSpeedDecreased`, `RocketExploded`, `RocketMissionChanged`).
- **`RocketMonitor`** (`IRocketMonitor`): one per rocket. It applies messages in whatever order they arrive and keeps the latest state. The rules give the same result for any arrival order: speed is a sum, the mission with the highest message number wins, and the first launch and first explosion win.
- **`MessageNumberTracker`** (a private class inside `RocketMonitor`): detects redelivered messages. It remembers a watermark (every message number up to N has been applied) plus a sorted set of the numbers applied above it.
- **`RocketState`**: an immutable snapshot of a rocket (type, speed, mission, status and so on). The monitor replaces it after every applied message, so readers never need a lock.
- **`IRocketRegistry`**: the interface for keeping track of every rocket seen so far. Its implementation is in `Rockets.Infrastructure`, because where the rockets are kept is a storage concern, not a business rule.
- **`RocketMonitorFactory`** (`IRocketMonitorFactory`): creates the monitor for a rocket. A registry in another project uses it, since `RocketMonitor` itself is internal to this project.

### `src/Rockets.Application`

The use cases, and the interfaces the outer projects implement.

- **`Messaging/IMessageChannel`**: the queue between whoever receives messages and the consumer. It also defines the exceptions for a full or closed channel.
- **`Messaging/IMessageListener`**: something that receives messages from a transport and writes them to the channel. `MessageListenersHostedService` starts and stops all registered listeners with the application.
- **`Consumers/RocketMessageConsumer`**: the single consumer. It reads the channel and applies each message to the right rocket's monitor. On shutdown it finishes processing everything still in the channel.
- **`Queries/RocketQueryService`** (`IRocketQueryService`): the report on one rocket.
- **`Queries/FleetReportService`** (`IFleetReportService`): the reports on all rockets: the sorted, filtered and paged list, and the summary counts.
- **`Queries/Dtos.cs`, `FleetQuery.cs`**: what the API returns and accepts (`RocketDto`, `PagedResult`, `FleetSummaryDto`, the sort and filter options). Domain types are converted to these here, so they never reach the API.

### `src/Rockets.Infrastructure`

Technical implementations of the Application interfaces.

- **`InMemoryMessageChannel`**: the default `IMessageChannel`, built on a bounded `System.Threading.Channels` channel. When it is full, a write waits briefly and then fails, which the listener turns into a 503. It never drops a message that was already accepted.
- **`MessageChannelOptions`**: the capacity and the write timeout.

- **`Rockets/RocketRegistry`**: the default `IRocketRegistry`. It keeps the rocket monitors in memory and asks the domain's `IRocketMonitorFactory` for a new monitor the first time a channel appears.

A Kafka-backed channel or a Redis-backed registry, for example, would be added here without changing the other projects.

### `src/Rockets.Listener.Http`

Receives messages over HTTP. It runs its own web server on port 8088, separate from the query API.

- **`HttpMessageListener`**: the HTTP implementation of `IMessageListener`. It builds and runs the web server, with the rate limiter (429) and its own Swagger page.
- **`Controllers/MessagesController`**: `POST /messages`. It answers 202 only after the message is in the channel.
- **`Controllers/ChannelUnavailableExceptionFilter`**: turns a full or closed channel into a 503 with `Retry-After`.
- **`Parsing/MessageEnvelope`, `RocketMessageMapper`**: the JSON shape of an incoming message, and its conversion to a domain message based on `messageType`.
- **`ListenerOptions.cs`**: the listener address and the rate limit settings.
- **`EmbeddedHostLifetime`**: stops this web server from reacting to Ctrl+C on its own, so the main application controls the shutdown order.

### `src/Rockets.Api`

The executable. It wires everything together and serves the query API on port 8080.

- **`Program.cs`**: registers all services, reads the configuration, and sets up Swagger and Serilog. It is the only place that knows which implementation is behind each interface.
- **`Controllers/RocketsController`**: `GET /api/rockets/{channel}`.
- **`Controllers/FleetController`**: `GET /api/fleet/rockets` and `GET /api/fleet/summary`.
- **`appsettings.json`**: the default configuration.

### `tests/`

One xUnit project per source project.

| Project | What it tests |
|---|---|
| `Rockets.Domain.Tests` | The state rules, duplicate detection, and that the final state does not depend on arrival order. |
| `Rockets.Application.Tests` | Sorting, filtering, paging and the summary; the single-rocket query; the consumer, including processing what is left in the channel when stopping. |
| `Rockets.Infrastructure.Tests` | The channel: ordering, failing when full, waiting for space, refusing writes once closed. The registry: one monitor per channel, unknown channels, listing all states. |
| `Rockets.Listener.Http.Tests` | The message endpoint's responses (202, 400, 429, 503) and the conversion of each message type. |
| `Rockets.Api.IntegrationTests` | The query endpoints through the real application, and that `/messages` is not exposed on the API port. |

## Tests

```bash
dotnet test
```

There are 55 tests. They cover:

- the domain rules, including an order-independence test: the same messages, shuffled and with duplicates, must give the same state;
- the channel's backpressure;
- the listener's status codes (202/400/429/503);
- the query API, through `WebApplicationFactory`.

### End-to-end verification

The service was also run against the real test program. Every message the program sends was recorded (its messages are deterministic for a given seed), and the expected state of each rocket was calculated with a separate, throwaway Python implementation of the rules. That was compared field by field with the service's API. The scripts were one-off and are not part of this repository.

Result with the default settings (100,000 messages, concurrency 3, no delay): all messages accepted in about 16 seconds, with no 429 or 503 responses, and **0 mismatches across all 20 rockets**. See "Verification" in [decisions.md](decisions.md) for the details, including a finding about the test program's redelivery.
