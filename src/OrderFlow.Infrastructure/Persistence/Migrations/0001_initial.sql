-- Orders: the write model. The version column is the optimistic-concurrency token.
CREATE TABLE orders (
    id                  uuid          PRIMARY KEY,
    customer_id         uuid          NOT NULL,
    status              text          NOT NULL CHECK (status IN ('Placed', 'Paid', 'Shipped', 'Cancelled')),
    currency            char(3)       NOT NULL,
    total               numeric(18,2) NOT NULL CHECK (total >= 0),
    payment_id          text,
    tracking_number     text,
    cancellation_reason text,
    idempotency_key     text          NOT NULL,
    version             integer       NOT NULL,
    placed_at           timestamptz   NOT NULL,
    updated_at          timestamptz   NOT NULL,
    CONSTRAINT uq_orders_idempotency_key UNIQUE (idempotency_key)
);

CREATE INDEX ix_orders_customer ON orders (customer_id, placed_at DESC);

CREATE TABLE order_lines (
    order_id   uuid          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    line_no    integer       NOT NULL,
    sku        text          NOT NULL,
    quantity   integer       NOT NULL CHECK (quantity > 0),
    unit_price numeric(18,2) NOT NULL CHECK (unit_price >= 0),
    PRIMARY KEY (order_id, line_no)
);

-- Transactional outbox: written in the same transaction as the state change, relayed later.
CREATE TABLE outbox_messages (
    id               uuid        PRIMARY KEY,
    type             text        NOT NULL,
    payload          jsonb       NOT NULL,
    trace_parent     text,
    occurred_at      timestamptz NOT NULL,
    attempts         integer     NOT NULL DEFAULT 0,
    next_attempt_at  timestamptz NOT NULL,
    processed_at     timestamptz,
    dead_lettered_at timestamptz,
    last_error       text
);

-- Partial index: the relay only ever scans pending messages, which stay a tiny fraction of the table.
CREATE INDEX ix_outbox_pending ON outbox_messages (next_attempt_at)
    WHERE processed_at IS NULL AND dead_lettered_at IS NULL;

-- Idempotent inbox: one row per (message, consumer) makes redelivered messages a no-op.
CREATE TABLE inbox_messages (
    message_id   uuid        NOT NULL,
    consumer     text        NOT NULL,
    processed_at timestamptz NOT NULL,
    PRIMARY KEY (message_id, consumer)
);
