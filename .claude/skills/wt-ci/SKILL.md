---
name: wt-ci
description: Use when changing or debugging WorkerTransfer's CI (`.github/workflows/ci.yml`, `backmerge.yml`) or package restore — the five jobs, the `images` job and `--wait`, the dependency audits and their language, `NuGet.Config` source mapping, a 401/403 on restore, a red run that looks like a broken feed, or a hotfix that has to come back to develop.
---

# CI and restore

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding rules — branch flow,
no `continue-on-error`, no Dependabot — stay there. How CI first went green on
the .NET branch (09.09.2026, four fixes) is history and lives in
`docs/GESCHICHTE.md`.

## Restore (from `## Commands`)

**Restoring needs no login any more.** Noelia went to nuget.org on 10.09.2026 (MIT, source at [DavidOeztuerk/girder](https://github.com/DavidOeztuerk/girder)), so `dotnet restore`, `docker compose up` and CI all work in a fresh clone with no token. Measured against an empty package cache: every Noelia assembly came back with `"source": "https://api.nuget.org/v3/index.json"`.

Before that it lived in GitHub Packages, and that cost a whole evening of CI — the notes are in the CI section, and the distinction worth keeping is **403 means authenticated and refused, 401 means never authenticated at all.**

`NuGet.Config` still pins package source mapping, and the reason has only shifted. It used to keep a foreign `Noelia.*` on nuget.org away from us; now it pins that exactly **one** source may answer those names. Without it, any additionally configured source — a company mirror, a local folder — is asked for every name, and whoever answers first wins. `<clear />` stays for the same reason: it discards the sources a developer machine carries in its user-level config.

(The CI notes referred to above now stand in `docs/GESCHICHTE.md`, „CI is green on the .NET branch as of 09.09.2026".)

### CI

`.github/workflows/ci.yml` has five jobs: `backend-quality` (restore → build → `scripts/test-dotnet.sh`, in separate steps), `frontend-quality` (check, test, **build**), `e2e` (the Playwright journeys against the full stack), `dependency-audit` (`dotnet list package --vulnerable` + `pnpm audit --prod`), and `images`.

`images` is the one that used to be missing, and its absence is why a Dependabot bump to `node:25` passed CI while breaking the web image. It builds both shipped images, brings the whole compose stack up with `--wait`, then asks three times through the gateway: `GET /jobs` (200 with the `items`/`next` page shape — the proof of routing is *whose* answer it is, not that it succeeded; a missing route would be Ocelot's empty 404, a dead service a 502, the UI would send HTML), `GET /consent/me` (401 plus an RFC 9457 document with `correlationId`), and `POST /auth/register` (201). The middle one exists because `/jobs` went public: it used to carry the problem-document proof on its way past, and afterwards nothing did. Only the write proves the migrations ran; the reads answer on an empty database too.

`--wait` is deliberate. An earlier draft started each service and checked `.State.Running` after three seconds; the counter-probe (take away the gateway's `ocelot.json`) showed a service that throws in `Program.Main` and *keeps running* at 99% CPU, still `Running=true` after 45 seconds. The check was green over a process producing nothing but a stack trace.

`dependency-audit` sets `DOTNET_CLI_UI_LANGUAGE: en` because it *reads* the output, and the same command answers German locally and English on the runner — a grep for one of the two sentences would be silently always-true in the other environment. It also fails if the scan named fewer than 50 projects: a scan that examined nothing is otherwise indistinguishable from a clean result. Neither audit runs with `continue-on-error`; a scan that is allowed to be red gets ignored after the second week, and is then worse than none.

**There is no `dependabot.yml`** (deleted 08.08.2026). This job is the only thing that asks.

The branch flow and the hotfix backmerge (`hotfix/…`, never `fix/…`) stand in
`CLAUDE.md`, „Branches and the one path back".
