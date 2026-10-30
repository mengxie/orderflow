# Architecture

This guide explains how OrderFlow keeps its promises. Decisions with real alternatives are recorded
separately as [ADRs](adr).

## Layers and their rules

| Project          | Owns                                                                 | May depend on                         |
| ---------------- | -------------------------------------------------------------------- | ------------------------------------- |
| `Domain`         | `Order` aggregate, value objects, domain events, `Result`/`Error`    | Nothing but the BCL                   |
| `Contracts`      | Versioned integration events and their wire names                   | Nothing but the BCL                   |
| `Application`    | Commands, queries, consumers, decorators, domain → contract mapping  | Domain, Contracts, logging/DI abstractions |
| `Infrastructure` | PostgreSQL, outbox relay, inbox, RabbitMQ, health checks             | Application (and so everything inward) |
| `Api`            | HTTP endpoints, problem details, OpenTelemetry, composition root     | Everything                            |

`ArchitectureTests` reads the compiled assemblies and fails the build if, for example, the domain
references `Microsoft.*` or the application references Npgsql.

## Write path: one transaction per command

Every command handler is registered behind two decorators:

```
InstrumentedCommandHandler  → span, duration metric, structured log
  TransactionalCommandHandler → BEGIN … COMMIT / ROLLBACK, concurrency conflict → 409
    PlaceOrderHandler         → domain logic only
```

`DbSession` is the scoped unit of work: one connection, one transaction, and a list of the
aggregates that repositories loaded or saved. `CommitAsync` dequeues each aggregate's domain
events, maps them to integration events and inserts them into `outbox_messages` **in the same
transaction** as the state change, then commits. Either the order change and its events are both
stored, or neither is.

Repositories refuse to write outside a transaction, so an event can never be skipped by
accident.

### Optimistic concurrency

Each state change increments the aggregate's `Version`. Updates are conditional:

```sql
UPDATE orders SET … , version = @Version WHERE id = @Id AND version = @ExpectedVersion
```

Zero rows updated means another request saved first; the repository throws
`ConcurrencyConflictException`, which the transactional decorator turns into a `409` problem.

### Idempotent order placement

`orders.idempotency_key` has a unique constraint. A retried request finds the existing order and
returns it with `200`. Two *concurrent* requests with the same key race on the constraint; the
loser gets `409` and can safely retry.

## Outbox relay

`OutboxRelay` is a hosted service that runs every `Outbox:PollInterval` (default 1 s) and keeps
going without waiting while a full batch comes back:

```sql
SELECT … FROM outbox_messages
WHERE processed_at IS NULL AND dead_lettered_at IS NULL AND next_attempt_at <= @Now
ORDER BY occurred_at
LIMIT @BatchSize
FOR UPDATE SKIP LOCKED
```

- **SKIP LOCKED** lets any number of instances relay concurrently: rows locked by one relay are
  invisible to the others. This was verified against PostgreSQL with two concurrent sessions.
- **Publisher confirms**: a publish only completes once RabbitMQ has the message, so a row is
  marked processed only after the broker accepted it.
- **Retries** use exponential backoff (2^attempt seconds, capped at `Outbox:MaxRetryDelay`).
  After `Outbox:MaxAttempts` the row is dead-lettered: kept for inspection, no longer retried.
- **Retention**: processed outbox rows and inbox rows older than `Outbox:Retention` (7 days)
  are deleted hourly.

Delivery is **at least once**: if the process dies after publishing but before committing the
batch, those messages are published again. Every message carries a stable `MessageId` so that
consumers can deduplicate, which is exactly what this service's own inbox does.

## Inbox: exactly-once effects on at-least-once delivery

The consumer acknowledges manually. For each delivery, `InboxProcessor` opens a transaction and:

1. `INSERT INTO inbox_messages (message_id, consumer) … ON CONFLICT DO NOTHING`.
   Zero rows means this consumer already processed the message: roll back, acknowledge, done.
2. Runs the consumer (`PaymentCapturedConsumer` → `RecordPaymentHandler` → `Order.MarkPaid`).
3. Commits: inbox row, order change and the `OrderPaidV1` outbox row together.

If two copies of a message are processed at the same moment, the second insert waits on the
primary key and then finds the row, so the effect still happens once.

| Outcome                     | Broker action                                                        |
| --------------------------- | -------------------------------------------------------------------- |
| Processed or duplicate      | `ack`                                                                |
| Unknown type, invalid JSON  | `nack` without requeue → dead-letter queue immediately               |
| Exception (e.g. DB down)    | `nack` with requeue; after `x-delivery-limit` (5) deliveries the quorum queue dead-letters it |
| Business rejection          | Logged and `ack`ed: a payment that does not match the order will never succeed on retry |

## Read path

Queries go through `IOrderQueries`, which maps rows straight to `OrderResponse` without
rehydrating the aggregate: the read and write models can evolve separately (CQRS).

## Distributed tracing

All telemetry uses `System.Diagnostics` (`ActivitySource`, `Meter`); only the API host knows about
OpenTelemetry. Trace context crosses every asynchronous hop:

1. The command span is the current activity when `DbSession` writes the outbox row, so its
   W3C `traceparent` is stored in `outbox_messages.trace_parent`.
2. The relay starts its publish span with that stored parent and injects the context into the
   RabbitMQ message headers via `DistributedContextPropagator`.
3. The consumer extracts it and starts its processing span as a child.

So an order placed over HTTP, the relay publishing its event seconds later, and the consumer
handling a reply all belong to one trace in the Aspire dashboard or any OTLP backend.

## Schema migrations

SQL scripts in `Infrastructure/Persistence/Migrations` are embedded in the assembly and applied
at startup, in order, in one transaction guarded by `pg_advisory_xact_lock`, so several instances
starting together cannot race. Applied versions are recorded in `schema_migrations`. Released
scripts are never edited; changes go in a new script.

## Configuration

| Key                               | Default                                     |
| --------------------------------- | ------------------------------------------- |
| `ConnectionStrings:Postgres`      | local docker-compose database               |
| `Database:MigrateOnStartup`       | `true`                                      |
| `Outbox:PollInterval`             | `00:00:01`                                  |
| `Outbox:BatchSize`                | `100`                                       |
| `Outbox:MaxAttempts`              | `10`                                        |
| `Outbox:LagWarningThreshold`      | `00:01:00` (readiness turns Degraded beyond it) |
| `RabbitMq:ConnectionString`       | `amqp://orderflow:orderflow@localhost:5672` |
| `RabbitMq:DeliveryLimit`          | `5`                                         |
| `OTEL_EXPORTER_OTLP_ENDPOINT`     | OTLP collector, e.g. the Aspire dashboard   |
