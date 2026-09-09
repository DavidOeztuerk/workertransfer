# Contributing

## Prerequisites

- .NET SDK 10
- Node 25 and pnpm (see `package.json` engines)
- Docker — the integration suites use Testcontainers, and **without it they skip themselves**, which looks exactly like passing
- A NuGet login for GitHub Packages, because Girder lives there:

  ```bash
  dotnet nuget add source https://nuget.pkg.github.com/DavidOeztuerk/index.json \
    --name GitHub --username <you> --password <token> --store-password-in-clear-text
  ```

## Checks

Run before pushing or merging — this is the binding order and the same one CI uses:

```bash
make check    # build → test → pnpm check → pnpm test → pnpm build, fail-fast
make fix      # dotnet format
```

Or the steps explicitly:

```bash
dotnet build WorkerTransfer.slnx     # warnings are errors
./scripts/test-dotnet.sh             # the suites, one at a time
pnpm check && pnpm test && pnpm build
```

Two rules that are not style preferences:

- **Build and test in separate invocations.** Chained, the Testcontainers suites fail and the output looks like real test failures.
- **Never `dotnet test` over the solution.** Fifteen suites start fifteen Postgres containers at once, the ResourceReaper times out, and all of them fail within a millisecond with `TypeInitializationException` — indistinguishable from a broken build.

`make validate` answers a different question than `make check`: it does not stop at the first failure, it reports every red step at once, it **names the skipped tests**, and it **prints how many tests actually ran**. A green run with twenty skips is not a green run.

## Writing changes

Keep each change focused and test the affected behaviour.

**Run a counter-probe for anything load-bearing**: break the rule deliberately, confirm exactly the right test falls, then revert. Two things go wrong here often enough to name:

- Make sure the break **compiles** — a build error reads in the output like a passing test.
- After the revert, build with **`--no-incremental`**. The incremental build has failed to notice a revert three times; once a counter-probe *passed* while the rule was genuinely missing, which is the dangerous direction because it looks like a licence.

**A counter-probe that does not fall reveals a weak test, not correct code.** Strengthen the test rather than dropping the claim.

**No detour around a Girder bug.** Write the code correctly, leave the test red, file a ticket in `bugs/` with a reproduction free of WorkerTransfer code, and move on. Everything in `bugs/` must go green once the bug is fixed, without anyone reverting code.

Update an ADR when a cross-cutting architectural decision changes, and add one in [docs/adr/](docs/adr/) when a change introduces a new one.

## What must not enter source control

Do not add real secrets, personal documents, access tokens, candidate data, CVs, contracts or provider payloads.

The same applies to logs: **shapes, not contents**. Girder logs no values either, neither redacted nor scrubbed — do not build that back.

## Branches

`feature → develop → main`, never a feature branch straight into main.

A **hotfix** is the single exception: it branches off `main`, merges into `main`, and is carried back by `.github/workflows/backmerge.yml`. That workflow triggers only on a merged PR whose head branch starts with **`hotfix/`** — the name is load-bearing. Call it `fix/…` and the run never fires, so the correction silently stays out of develop.

## Where the reasons live

[CLAUDE.md](CLAUDE.md) collects the rules that carry the design, each with its reason — read it before changing anything about consent, erasure, skills or the AI seam. [AGENTS.md](AGENTS.md) is the same content in one line per rule. [docs/product-scope.md](docs/product-scope.md) holds the product guardrails, [docs/architecture.md](docs/architecture.md) the service shape, and [docs/MIGRATION-STAND.md](docs/MIGRATION-STAND.md) what was measured while the system was translated from Python.
