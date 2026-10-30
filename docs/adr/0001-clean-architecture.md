# ADR 0001: Clean Architecture with a dependency-free domain, enforced by tests

- Status: Accepted
- Date: 2026-10-30

## Context

The business rules of ordering (what makes an order valid, which transitions are legal) change for
business reasons. Databases, brokers and web frameworks change for technical reasons. Mixing them
makes both harder to change and the rules hard to test.

## Decision

Five projects with dependencies pointing inwards: Domain and Contracts at the centre with no
dependencies beyond the BCL, Application around them, then Infrastructure and the API host.
Architecture tests read the compiled assemblies and fail the build on a violation.

Contracts are a separate project from the domain so that other services can reference them
without taking a dependency on this service's internals.

## Consequences

- The domain is tested with plain unit tests: no mocks, no database, milliseconds per test.
- The rules survive code review fatigue because a test, not a reviewer, guards them.
- More projects and some mapping code (domain events → integration events, rows → snapshots).
  For a service this size that is a deliberate trade for clarity, not a necessity.
