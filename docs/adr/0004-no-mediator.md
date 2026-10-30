# ADR 0004: No mediator library: handlers and decorators

- Status: Accepted
- Date: 2026-10-30

## Context

CQRS in .NET is often built on a mediator library that resolves handlers at runtime and runs
pipeline behaviours. That indirection hides which code runs for a request, makes "go to
definition" stop at `Send`, and adds a dependency whose licensing has changed in the past.

## Decision

Endpoints depend directly on `ICommandHandler<TCommand, TResponse>`. Cross-cutting concerns are
explicit decorators composed once, at registration:

```
InstrumentedCommandHandler → TransactionalCommandHandler → concrete handler
```

The concrete handler is also registered on its own, so code that already owns a transaction
(the inbox) can call it without opening a nested one.

## Consequences

- The full pipeline for a command is visible in one method (`AddCommand` in `DependencyInjection`).
- No runtime reflection on the request path, and no third-party dependency.
- Adding a command means one registration line; a test verifies the composed pipeline.
