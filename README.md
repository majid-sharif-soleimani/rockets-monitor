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

## Project structure

```
src/
  Rockets.Domain           Messages, RocketState, RocketMonitor (order-independent rules,
                           duplicate detection), RocketRegistry
  Rockets.Application      IMessageChannel, IMessageListener, the consumer, rocket and fleet
                           report services, DTOs
  Rockets.Infrastructure   Bounded in-memory channel (System.Threading.Channels)
  Rockets.Listener.Http    HTTP listener on its own port: parsing, rate limiting, 503 mapping
  Rockets.Api              Composition root, query controllers, Swagger, Serilog
tests/                     One xUnit project per source project
tools/e2e/                 End-to-end verification scripts (see below)
```

Message flow: `HttpMessageListener` → `IMessageChannel` → `RocketMessageConsumer` → `RocketMonitor` (one per rocket) → immutable `RocketState` snapshots → query services → controllers.

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

The state the service computes is compared with an independent Python implementation that works from the exact messages the test program sends. The program's messages are deterministic for a given seed.

```bash
# 1. Record what the test program sends (any free port)
python3 tools/e2e/capture.py 9099 captured.jsonl &
./rockets launch "http://127.0.0.1:9099/messages"
kill %1

# 2. Run the service, send the same messages to it, then compare
dotnet run --project src/Rockets.Api -c Release &
./rockets launch "http://localhost:8088/messages"
python3 tools/e2e/compare.py captured.jsonl http://localhost:8080
```

Result with the default settings (100,000 messages, concurrency 3, no delay): all messages accepted in about 16 seconds, with no 429 or 503 responses, and **0 mismatches across all 20 rockets**. See "Verification" in [decisions.md](decisions.md) for the details, including a finding about the test program's redelivery.
