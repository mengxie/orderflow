# ADR 0006: Result values for expected failures

- Status: Accepted
- Date: 2026-10-30

## Context

Many failures are normal business outcomes: an unknown SKU, paying the wrong amount, shipping an
unpaid order. Modelling them as exceptions makes control flow implicit, is slow, and blurs the
line between "the request was wrong" and "the system is broken".

## Decision

Domain and application methods return `Result` / `Result<T>` carrying an `Error` with a stable
`Code`, a message and an `ErrorKind` (`Validation`, `NotFound`, `Conflict`). The API maps the kind
to an HTTP status and returns RFC 9457 problem details with the code. Exceptions are reserved for
programming errors and infrastructure faults, which the global exception handler turns into `500`.

## Consequences

- Callers must handle failure explicitly; nullable annotations (`MemberNotNullWhen`) make the
  compiler check it.
- Clients get machine-readable codes such as `order.invalid_transition` to branch on.
- Slightly more verbose code than throwing, in exchange for explicit, testable outcomes.
