# ADR 0003: Idempotent inbox for at-least-once delivery

- Status: Accepted
- Date: 2026-10-30

## Context

RabbitMQ, like every practical broker, delivers at least once: after a consumer crash, a lost ack
or a network blip, a message arrives again. Applying a payment twice must not happen.

## Decision

Before handling a message, insert `(message_id, consumer)` into `inbox_messages` in the same
transaction as the handler's changes and any events it raises. A conflicting insert means the
message was already processed, so it is acknowledged and skipped. Acknowledgement happens only
after commit.

The domain is also idempotent where it is cheap (`MarkPaid` with the same payment id is a
no-op), as defence in depth.

Poison messages are handled by the broker: the inbound queue is a quorum queue with
`x-delivery-limit`, after which messages move to a dead-letter queue. Malformed messages and
unknown types are rejected to the dead-letter queue immediately.

## Consequences

- Exactly-once *effects* on top of at-least-once *delivery*.
- One extra insert per message, and a table that needs retention longer than any plausible
  redelivery window (7 days).
- Business rejections (for example a payment for the wrong amount) are logged and acknowledged
  rather than retried, because retrying cannot change the outcome.
