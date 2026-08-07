# Vision documents

**These describe the destination, not the current state.** Read them for intent;
read [`docs/ROADMAP.md`](../ROADMAP.md) for what actually exists today.

| File | What it is |
|---|---|
| [`kon.txt`](kon.txt) | The original conversation that defined WorkerTransfer: an AI-first Workforce Operating System. Source of the transfer market, the consent-first stance, GitHub as a verified competence source, the 23 draft-only agents, career connectors instead of scraping, and personalised landing pages. |
| [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) | A tabular restatement of the same vision: 30+ shared packages, 21 services, agent categories. Useful mainly as the naming reference for services still to be built. Its "Current State" section is stale — it predates Phase 2. |

Until 2026-07-31 both files were gitignored, so no clone contained them and every
new contributor (human or agent) worked from second-hand summaries.

## Where the implementation deliberately diverges

The vision is not followed literally. Three documented deviations:

1. **No fluent `PlatformBuilder`.** `kon.txt` sketches
   `PlatformBuilder().add_logging().add_database()…`. [ADR-0003](../adr/0003-composition-root-not-fluent-builder.md)
   rejects it: hidden ordering dependencies and an unclear "what is actually on"
   are dangerous in a consent- and security-first product. Services use an
   explicit Composition-Root instead.
2. **No scraping, ever.** `kon.txt` already says this, and
   [ADR-0004](../adr/0004-contracts-no-scraping-consent-first.md) makes it
   enforceable: a connector needs an official API or documented feed plus its own
   ADR recording source, scopes, sync cadence and deletion behaviour.
3. **AI drafts, humans decide.** Every one of the 23 agents is draft-only. No
   autonomous ranking, contacting, rejecting or negotiating about people.

## The rule that is easy to lose

> **kon.txt, Regel Nr. 1:** *"Wir schreiben keinen einzigen Microservice, bevor
> die Plattform existiert."*

The point of Phase 0 was that `worker new-service <name>` produces a ready
service, so services 3 through 21 are never hand-copied. That rule was slipping:
`apps/consent-service` was scaffolded by hand from `apps/identity-service`, and
the generator emitted code that did not parse. Phase 2.5 repaired the generator;
keep new services coming out of it.
