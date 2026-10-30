# ADR 0005: Dapper and SQL migrations instead of EF Core

- Status: Accepted
- Date: 2026-10-30

## Context

The interesting persistence in this service is precise SQL: conditional updates for optimistic
concurrency, `FOR UPDATE SKIP LOCKED` batch claims, `ON CONFLICT DO NOTHING` for the inbox, a
partial index for pending outbox rows, and an advisory lock for migrations. EF Core can express
most of this, but through raw SQL or provider-specific escape hatches that hide the important part.

## Decision

Use Dapper over a pooled `NpgsqlDataSource`, with all SQL in one reviewable file (`Sql.cs`).
The aggregate is persisted through a snapshot record, keeping the domain free of ORM concerns.
Schema changes are plain SQL scripts, embedded in the assembly and applied in order at startup
under `pg_advisory_xact_lock`.

## Consequences

- Every query is visible and was validated against PostgreSQL; the integration tests exercise
  all of them against a real database.
- More mapping code than an ORM, and no change tracking: repositories write explicitly.
- Migrations are forward-only scripts; rollbacks are new scripts.
