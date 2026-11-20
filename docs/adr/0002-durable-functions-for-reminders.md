# 2. Durable Functions orchestration for reminders, not a queue worker

Date: 2026-11-20

## Status
Accepted

## Context
A reminder is a small workflow per dose: sleep until due, remind, wait for the user's answer with a deadline, remind
again, give up and alert a caregiver, repeat forever. Sibling projects solve "do X later" differently:
*upkeep* uses BullMQ delayed jobs (Redis), *qirsh* uses RabbitMQ consumers with an outbox. With a job queue, the
workflow state (which step, which deadline, was it answered) has to be stored and re-derived by each job by hand.

## Decision
One **eternal Durable Functions orchestration** per schedule (`reminders-{scheduleId}`):
durable timer → `SendReminder` activity → `WaitForExternalEvent("DoseAnswered")` raced against a durable timer
(human-interaction pattern) → escalation → `MarkMissed` → `ContinueAsNew`. Snooze is a payload on the same
event that restarts the clock after a durable timer (bounded to 3 per dose). The HTTP API raises the event when the
user taps Take/Skip/Snooze. The weekly adherence report is a **fan-out/fan-in** orchestration (one activity per schedule).
Activities stay thin and call application services, so all decisions are unit-testable without the host.

## Consequences
- The workflow reads top to bottom as ordinary async C#; state, timers and retries are persisted by the Durable Task
  framework (event sourcing into Azure Storage tables/queues — Azurite locally).
- Orchestrator code must be deterministic: no `DateTime.UtcNow` (use `context.CurrentUtcDateTime`), no I/O, no
  thread-pool continuations. We hit this for real: `CancellationTokenSource.CancelAsync()` resumed the orchestrator
  off its scheduler and hung the "answered" path; a synchronous `Cancel()` fixed it (see `DoseReminderOrchestrator`).
- `ContinueAsNew` keeps history bounded; the Durable analyzer (DURABLE0011) forced the answer-wait loop to be bounded too.
- Answers arriving for another dose (stale/early) are ignored by comparing the event's due time.
- Lock-in: this is Azure Functions specific. The domain and application layers are host-agnostic; porting means
  rewriting ~150 lines of orchestration/trigger code (e.g. to Temporal or a BullMQ flow).
