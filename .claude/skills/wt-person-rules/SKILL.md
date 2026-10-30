---
name: wt-person-rules
description: Use before building anything in WorkerTransfer that looks at, compares, searches or describes a person — what ADR-0022 actually forbids (and permits), the skill vocabulary in `WorkerTransfer.Skills` (ADR-0023, renames, never infers), and fit as a browser-only checklist (`match.ts`). Read it before adding a score, a ranking, a match endpoint, a derived attribute or a skill alias.
---

# What may be said about a person

Moved verbatim from `CLAUDE.md` (30.10.2026). Each rule also stands as one binding
line in `CLAUDE.md`, „The rules that carry the design".

### The skill vocabulary renames, it never infers (ADR-0023)

`src/shared/WorkerTransfer.Skills` is a table plus a function, applied *inside* the skill value objects of `profile-service` and `jobs-service` — not in an endpoint, so it also covers rows written before it existed. Order is load-bearing: canonicalise first, deduplicate second.

`"Postgres" == "PostgreSQL"` is a statement about *language* and is allowed. `"React implies JavaScript"` is a statement about a *person* and is forbidden — it credits someone with a skill they did not claim, where they cannot object. No level, no weight, no kinship, no likelihood-to-switch. It never rejects and never invents: what it does not know stays exactly as typed, because a list of permitted skills would be a claim about which work exists. A test fails if the module ever exposes a name containing `level`/`weight`/`score`/`rank`/`implies`.

### Fit is computed in the browser and exists nowhere else

Job skills and profile skills are compared in `web/src/features/work/lib/match.ts`; the result is a checklist ("Du hast 2 von 3 genannten Fähigkeiten: Python ✓ · Kubernetes ✓ · Go ✗") shown **to the person, never to the company**, and it ranks *jobs*, not people.

There is deliberately no score, no percentage, no match endpoint and no match column. A ranked candidate list with a percentage is exactly what ADR-0022 forbids, arriving through the back door — and a percentage hides the only thing that helps: *which* skill is missing. Do not add a server-side match, a sort by fit, or a "0 von 3" for someone who entered nothing: they said nothing, which is not the same as knowing nothing. Job skills use the *same* limits as profile skills, and a test pins that a job may never demand a skill longer than a profile can hold — such a line could never become a tick.

### What ADR-0022 actually forbids

**Read this before building anything that looks at a person.** ADR-0022 is routinely read as a ban on the whole subject. It is not, and reading it that way has already cost one wrong decision.

The ADR deleted a package that computed a person into **one number between 0 and 100** from ten dimensions with fixed weights, and that measured skill in a language as a share of written bytes. It forbids **exactly three things**, and all three still hold:

1. **A number that summarises a person — and any ranking derived from it.** The reason survives the formula: *"nobody justified the weights. They are an opinion in the costume of a formula."* And a decimal place looks like a measurement.
2. **Derived attributes without a basis** — "leadership", "community", "architecture" out of repository metadata; or skill as `bytes / total_bytes`, where a checked-in dependency beats a careful library and whoever writes little and well loses.
3. **Silent completeness.** *"Whoever has nothing on GitHub is not worse, but elsewhere. A view that does not say so lies by omission."*

The same ADR has a section called *"what may come back in Phase 6"*, and it is explicit:

- **Evidence with provenance** — "made *these* commits on *this* project", checkable, with a link, **without an intermediate calculation**.
- **Consent first.** A person connects their own account; the platform looks at nothing and reads about nobody who was not asked (ADR-0004: no scraping).
- **Visibility through the ledger**, like everything else — a capability, revocable at any time, effective immediately (ADR-0013).

So the line is not "no analysis". It is the **direction of the question**: requirement in, evidence out — never person in, number out. A tick says *which* skill is missing and can be contradicted; a number hides exactly that. `github-service` exists and was built *under* this ADR, not condemned by it — its domain module says so in its first paragraph, and `Adr0022Tests` scans its domain and contract assemblies for the forbidden vocabulary.
