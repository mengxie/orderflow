# ADR 0002: Transactional outbox instead of dual writes

- Status: Accepted
- Date: 2026-10-30

## Context

Placing an order must both save it and publish `OrderPlaced`. Writing to PostgreSQL and then
publishing to RabbitMQ is a *dual write*: a crash, timeout or broker outage between the two loses
the event or announces an order that was never saved. Distributed transactions (2PC) are not
supported by RabbitMQ and would couple availability of both systems anyway.

## Decision

Write integration events to an `outbox_messages` table in the same database transaction as the
state change. A background relay publishes them to RabbitMQ with publisher confirms and marks
them processed. Batches are claimed with `FOR UPDATE SKIP LOCKED` so several instances can relay
in parallel. Failed publishes back off exponentially and are dead-lettered after a maximum number
of attempts. The W3C trace context is stored with each row so traces survive the hop.

Considered alternatives: change data capture with Debezium (excellent, but heavy infrastructure
for one service) and broker-first event sourcing (a larger model change than this domain needs).

## Consequences

- An event is published if and only if its state change committed.
- Delivery is at least once and not strictly ordered across relays; consumers must deduplicate
  (every message has a stable id) and use `OrderVersion` for ordering.
- Publishing is delayed by up to the poll interval (1 s by default).
- The outbox table needs retention; processed rows are deleted after 7 days.
