# 3. Azure Table Storage behind repository ports

Date: 2026-11-20

## Status
Accepted

## Context
Data is small and access patterns are known: medications and schedules by id, dose events by schedule and time range,
newest notifications. The Functions host already needs a storage account (and Durable Functions keeps its task hub in it).

## Decision
Use **Azure Table Storage** (`Azure.Data.Tables`) through the application's repository ports. Keys are designed for the
queries: dose events use `PartitionKey = scheduleId`, `RowKey = due instant as sortable UTC text`, so a week is one range
query and re-recording a dose is an idempotent upsert. Logs use an inverted-ticks row key so a plain query returns
newest first. Notification log inserts treat 409 Conflict as "already delivered" (queue delivery is at-least-once).

## Consequences
- One storage account for everything; costs cents; Azurite emulates it locally and in Testcontainers.
- No joins, no ad-hoc queries, no transactions across partitions: adherence is computed in code from range queries.
- A relational store (Postgres via EF Core) would be a new adapter behind the same ports; the architecture tests keep
  Domain/Application free of Azure types so that swap stays mechanical.
