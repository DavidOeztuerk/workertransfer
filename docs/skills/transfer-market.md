# Skill: Transfer-Market (the football-style mobility market — Phase 5)

## Purpose
Implement the market-status state machine and the consensual offer/transfer
flow that is WorkerTransfer's differentiator. Both paths (employed candidate,
unemployed candidate) are consent-gated; rejection is always possible.

## When to use
Phase 5 and any later work on `apps/transfer-service`, contract generation, and
the Negotiation/Contract agents.

## Market-status state machine
```
Open → Listening → Unavailable → Under Contract → Transfer Listed
                                              → Negotiating → Transferred
```
- Transitions are events persisted via the outbox (ADR/Phase 9); the state
  machine is modelled in the domain layer, not inferred from flags.
- Illegal transitions raise a `DomainError` (worker-core) that the application
  layer maps to an RFC 9457 problem (worker-platform).

## Two consent paths
1. **Employed candidate:** Company contacts the candidate; an offer is made;
   the current employer participates; a transfer fee / bonus / start date is
   negotiated; a contract draft is produced; digital signature. The candidate
   consents at contact *and* at contract; the current employer consents at the
   transfer-step. Either party may stop the process at their own gates.
2. **Unemployed / free candidate:** Company contacts directly and makes an
   offer; no current employer; candidate consents at contact and contract.

In both paths the candidate can always reject; nothing is binding until
consented and signed.

## AI role (draft-only)
- **Transfer-Advisor agent** (candidate side) — drafts guidance between
  parties; does not negotiate autonomously.
- **Scout agent** (company side) — finds and explains candidates; does not
  contact autonomously.
- **Contract agent** — drafts transfer/employment/NDA/termination/amendment
  contracts; **draft-only**, jurisdiction-specific legal review required before
  production use; every draft carries a "requires legal review" notice.

## Contract generation contract
- Template via `worker-templates` (Jinja2/WeasyPrint).
- Audit trail per contract version (Phase 8).
- E-signature via approved provider (MCP/SDK); never stores secrets in repo.

## Tests (mandatory)
- All legal state transitions allowed; all illegal ones raise `DomainError`.
- Happy path end-to-end (employed): interest → offer → fee → bonus → start
  date → draft → sign, with consent checks at each gate.
- Rejection paths: candidate rejects, current employer refuses, contract
  expires/withdrawn — each lands back in a legal status.
- No offer/contact proceeds without candidate consent (consent-ledger.md).
