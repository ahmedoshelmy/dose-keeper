# 4. Flex Consumption hosting, cost model and cold starts

Date: 2026-11-20

## Status
Accepted

## Context
A personal reminder app is idle almost all the time and bursts a few times a day. Container Apps (used by the sibling
repos) bill per active replica; Functions Consumption bills per execution and GB-second.

## Decision
Target **Azure Functions Flex Consumption** (`infra/main.bicep`, compile-only in CI): Linux, .NET 10 isolated worker,
per-execution billing with a monthly free grant, `maximumInstanceCount` capped at 10, system-assigned identity to
storage (no keys, `allowSharedKeyAccess: false`), Application Insights with AAD auth.

## Consequences
- Idle cost is ~zero; a few thousand executions a day stay inside the free grant (storage transactions cost cents).
- **Cold starts**: the first HTTP call after idle can take seconds. Acceptable for a reminder app; the Durable timers
  wake the app themselves. If needed, Flex "always ready" instances remove it at a fixed cost.
- Durable timers survive scale-to-zero because state lives in storage, not in memory.
- HTTP functions use `AuthorizationLevel.Anonymous` for the local single-user demo; a real deployment puts Easy Auth
  (Entra ID) or API Management in front. Not deployed by this repo.
