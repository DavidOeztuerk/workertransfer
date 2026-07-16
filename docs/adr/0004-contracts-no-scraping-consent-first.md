# ADR-0004: Versioned contracts, no scraping, consent-first data acquisition

- **Status:** Accepted
- **Date:** 2026-07-12
- **Relates:** `docs/product-scope.md`, ADR-0002

## Context

`kon.txt` lists many external integrations: GitHub, LinkedIn, Xing, ATS
providers (Greenhouse, Lever, Personio, …), e-signature (DocuSign, Adobe),
calendar/meeting tools, CRM/HR systems, storage, and LLM providers. It also
describes "scrawling" in the earlier JobPilot idea and AI scouts that ingest
candidate signals.

`docs/product-scope.md` already states the product guardrails: no scraping
without an official API/feed/written permission; candidate controls visibility
and consent; GitHub import uses OAuth with user-approved scopes and is
reviewable/revocable/deletable; no credentials/tokens/CVs/contracts/raw source
in the repo or logs.

Today there is no `worker-contracts` convention enforcement and no connector
policy. Without one, the breadth of planned integrations would inevitably
produce ad-hoc adapters and a shared cross-service domain model.

## Decision

1. **Contracts are versioned boundary types, never a shared domain model.**
   `worker-contracts` holds pydantic DTOs and Integration-Event schemas used
   *between* services or with external systems. Business entities live only in
   the owning service. There is no shared database and no cross-service
   repository abstraction.
2. **No scraping.** A connector may be built only when the source provides an
   official API, a documented feed, or explicit written permission. Each
   connector requires a **connector ADR** recording: source, granted scopes,
   sync cadence, permission model, and **deletion behaviour**. Without such
   an ADR the connector is not built.
3. **Consent is an enabler, not a feature.** Profile visibility, document
   attachment, employer contact, GitHub import, and AI analysis all require a
   checked Consent-Ledger entry before they proceed. Consent is revocable and
   deletable; revocation must immediately withdraw the affected capability.
4. **No secrets in the repo or logs.** Credentials, tokens, CVs, contracts, and
   raw source-code content never enter source control or log output. Provider
   keys are runtime-only secrets.
5. **AI drafts, humans decide.** AI output — applications, messages, contract
   templates, summaries — is produced as a draft requiring a human review/approve
   step before any external send or legal document. AI never autonomously
   rejects, ranks, contacts, or makes employment decisions about people;
   matching evidence is shown as explainable, user-controlled signals, never as
   a single hidden "employability" score.
6. **Contract/legal templates require jurisdiction-specific legal review** before
   production use.

## Consequences

- Phase 4 (career-site connectors) and Phase 6 (developer intelligence / GitHub)
  proceed only after the matching connector ADR + consent path exist. This
  sequences correctly with the consent-ledger built in Phase 3.
- `worker-contracts` gains a versioning scheme (V1/V2 suffixes) and a contract
  test discipline so consumers can pin to a contract version.
- A scanner/CI guard may be added to reject `httpx`/`requests`-based scraping
  against domains with no approved connector ADR (future hardening; not part of
  Phase 1).
- The "AI Scout finds candidates from LinkedIn/Xing" idea from `kon.txt` is
  constrained to sources with an approved connector and consent; unsanctioned
  ingestion is explicitly out of scope and rejected on principle.

## Verification

Any connector ships with a linked ADR path documented in its package readme, and
integration tests assert that a feature gated by consent fails when consent is
absent or revoked.
