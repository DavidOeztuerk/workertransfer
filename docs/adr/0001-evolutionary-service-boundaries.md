# ADR 0001: Start with an evolutionary service architecture

- **Status:** Accepted
- **Date:** 2026-07-11

## Context

WorkerTransfer has several future domains: identity, profiles, companies, jobs,
applications, transfers, contracts, notifications, documents, search, integrations,
and AI workflows. Creating a deployable microservice and a shared package for every
future noun before any workflow is proven would multiply deployment, schema,
observability, testing, and security work without validating product behaviour.

## Decision

Use a monorepo with independently packaged, deployable entry points and strict Clean
Architecture boundaries. Begin with a small technical platform kernel and an identity
reference service. Add domains as vertical slices; extract independently deployed
services only when ownership, data, throughput, reliability, or release cadence makes
the boundary valuable.

Shared packages may contain only technical abstractions. Business entities, DTOs, and
database repositories are never placed in the platform kernel.

## Consequences

- A new service gets consistent configuration, health, logging, correlation, tenant
  context, error handling, and CQRS without copy-pasting.
- The system can adopt an outbox/inbox event model when persistent cross-service
  workflows are introduced.
- The initial codebase has fewer moving parts and a meaningful test surface.
- A future extraction must preserve its data ownership and published event contracts;
  moving a module is not enough to create a service boundary.
