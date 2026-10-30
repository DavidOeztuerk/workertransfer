---
name: wt-transfer
description: Use when working on WorkerTransfer's transfer-service and the way to a person's current employer (ADR-0050) — the employer's company setting, naming the employer as consent (`employer.route:tenant:<X>`, `employer_routes`), the single 404, the release negotiation between two companies, the earliest regular start from the notice period, `Retention.KeepPaid`, or `transfer_update`.
---

# transfer-service: the way to the current employer

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### The way to the current employer: two consents, and a negotiation between companies

Built 16.10.2026 under [ADR-0050](docs/adr/0050-der-alte-arbeitgeber-mit-zwei-zustimmungen.md) (VISION-FRAGEN C1, C3, D1). It ends the transfer draft's "the platform never contacts the employer" — **only** where both allow it.

- **The employer's half** is a company setting, `admin`, visible nowhere outside the company, without a word like "insolvency". It decides only whether a *new* inquiry may be made.
- **The person's half: naming the employer IS the consent.** She does it on the employer's careers page; `employer_routes` (a `SubjectKeyedTable`) is only the pointer, the consent is `employer.route:tenant:<X>` in the ledger, asked at every company read and step (ADR-0013). Closing the route deletes "works at X" and ends running negotiations. Without both halves every company sees **one 404** — no route, employer not asking, consent revoked, own employer: told apart, each would say where she works.
- **The employer sees a person only when a company asks** — her civil name through identity's person door, *after* the ledger check. There is no list of who opened a route; that list would be "who is listening".
- **The release negotiation** (`release_negotiations`, one per transfer): a month and a fee or none, terms set alternately, accepted only by the side that did not set them; then **she agrees last** and can decline in every running state. Both companies see her earliest regular start from her notice period (H2) — the one place a company sees it. **That date, and every other one derived from the notice period** (the "starts before your earliest change" mark, the advisor's facts), **exists only while her saved market status says employed**: a notice period binds somebody with a job, and one stated before she left it must not hold her back afterwards. The statement itself stays hers to read and withdraw. The platform suggests no price and moves no money.
- **The offer to the person carries no fee any more**; `Retention.KeepPaid` now means "a completed transfer with a settled negotiation that has a fee" — still `false`.
- Company steps notify the person and the other company's members as `transfer_update`; her own steps notify nobody.
