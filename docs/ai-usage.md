# How AI Was Used

> Draft, written from the session record. The reflection section at the end is for the author to complete.

The work used one AI coding agent (Claude Code), in a fixed loop: **analysis, then design suggestions, then a written plan, then review, then implementation in small committed steps**. The agent wrote no code until the plan had been reviewed over several rounds and explicitly approved.

## Who decided what

**Decided by the author** (requirements in `plan 1.md`, later answers in the design discussion):
- the architecture: a listener abstraction, a channel abstraction (in-memory now, Kafka-ready), a rocket monitor per rocket, and a separate registry and report layer;
- 429/503 responses with `Retry-After`, and a configurable channel capacity;
- separate ports for receiving messages and for the API, and a global rate limit;
- **calculating state without ever waiting for a missing message** (replacing the agent's first proposal, a buffer that waited for gaps);
- **no stored messages**: a watermark plus a sorted set for duplicate detection; the first received message wins;
- **reports contain only user-facing data** (the agent had proposed showing gap and completeness information);
- console-only logging (a rolling file was planned at first).

**Delegated to the agent:**
- reading the challenge and checking the toolchain and the test program's default settings;
- proposing designs and presenting the trade-offs, written up in `plan 2.md` and `decisions.md`;
- implementing it test-first, one commit per step;
- the end-to-end verification (one-off scripts, not kept in the repository) and tuning the defaults.

## Where agent proposals were overridden

| Agent proposal | Outcome |
|---|---|
| A sorted buffer that applies messages only once the gap before them is filled | Rejected. State must never wait, which led to the order-independent rules (DEC-07). |
| Storing messages per rocket, so state could be rebuilt | Rejected for simplicity. Watermark plus sorted set instead (DEC-08). |
| Lowest message number wins for a duplicate launch or explosion | Rejected. First received wins (DEC-09). |
| Showing gap and completeness data in the API | Rejected: users don't care about internal state (DEC-11). |
| A rolling log file | Dropped in favour of console only (DEC-12). |
| Keeping the end-to-end verification scripts in the repository (`tools/`) | Removed at the author's request. |

## How correctness was checked

- **Tests before code** for each layer: 55 tests, including an order-independence test with shuffled and duplicated messages.
- **Checking against the real program, not only against our own tests.** The program's messages were captured and replayed against an independent Python implementation of the rules, and the results compared field by field: 0 mismatches.
- **Measuring before choosing defaults.** The rate limit was set from the observed peak (~11k messages/s) after the first default turned out to cause data loss (DEC-14).

## Problems found during implementation

1. **The rate limit default was guessed, and it was wrong.** The end-to-end run showed that the program drops rejected messages when it exits. A unit test could never have found this; it needed a run against the real program.
2. **A shutdown race in `BackgroundService` (DEC-15).** The consumer test failed intermittently. The agent first committed a step while that test was failing, because its command checked only that test output was printed, not the test result. The failure was caught in the next step, the cause was found and fixed, the test was run 20 times in a row, and from then on commits were made only after a successful `dotnet test`.
3. **The listener's embedded app handled Ctrl+C itself (DEC-16).** This was visible as a duplicated shutdown line in the log during the end-to-end run.

## Reflection (to be completed by the author)

- What worked well:
- What was risky:
- What I would do differently in a larger or production system:
