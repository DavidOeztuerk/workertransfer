---
name: wt-jobs-sources
description: Use when working on adverts imported from a company's ATS in WorkerTransfer's jobs-service (ADR-0052) — Greenhouse, Lever, Personio or a schema.org `JobPosting` feed, proving a board with a code, the daily round (`SourceSchedule`), widening the egress boundary (`CompanyDomainEgress`), the `company-site` client and redirects, or what happens to mirrored adverts.
---

# jobs-service: adverts from a company's ATS

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### Adverts from a company's ATS: official reads, and the board proves itself with a code

Built 18.10.2026 under [ADR-0052](../../../docs/adr/0052-inserate-von-aussen-nur-offizielle-abrufe-mit-nachweis.md) (VISION-FRAGEN-2 L1–L5). jobs-service reads Greenhouse, Lever and Personio over their public APIs and a company's own schema.org `JobPosting` feed — never a scraped page.

- **A board name proves nothing**: every board is public, and anybody can type its name. A DNS record or a link on the company's own page proves nothing either — the company controls those, not the board (that was decided twice in round 2 and corrected). The proof is a **code** the platform hands out and the company writes into one advert at its ATS; the read that finds it binds the board and imports in the same step. **An unproven source imports nothing**, and the daily round (`SourceSchedule`, hourly, sources older than a day) reads only proven ones. The code belongs to the source, not the board, so a second company naming the same board gets its own. A feed is proven by lying on the proven domain (ADR-0019).
- **The egress boundary is widened through Noelia's interface**, never around it: `CompanyDomainEgress` replaces the registered `IEgressPolicy` instance and adds hosts below `Sources__Personio` and the proven domains of companies with a feed, https only. `SourceJourneyTests` resolves the policy from the host and fails if it is not the widened one — Noelia's guard resolves it from DI per client.
- **A feed must not reach inside**: the `company-site` client checks the resolved address at connect time (`HttpCompanySite.PublicOnly`) and follows redirects by hand, on the same domain only.
- **Mirrored adverts**: the text belongs to the source (409 on a change here); skills, salary and postal code the company adds here. Gone at the source closes a published advert and deletes a draft; closed stays closed; a failed read changes nothing; only an https link is kept as `source_url`. Removing a source hands its adverts over as the company's own.
