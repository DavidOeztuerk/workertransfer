# Geschichte aus der CLAUDE.md

Am 30.10.2026 ist die `CLAUDE.md` auf den Kern verkleinert worden (Gate G-0015).
Was dort Geschichte oder Herleitung war, steht seither hier — **wörtlich**, in der
Sprache des Abschnitts. Die Regeln, die daraus folgen, stehen als kurze,
verbindliche Zeilen in der `CLAUDE.md`; das Aufgabenwissen in den Skills unter
`.claude/skills/`.

## What this is

The repository was a Python (`uv`) monorepo until August 2026 and was translated by hand, service by service. **Everything Python is gone**: no `pyproject.toml`, no `uv.lock`, no `apps/<service>`, no `packages/worker-*`, no alembic, ruff, mypy or pytest. `docs/MIGRATION-STAND.md` records what was decided along the way and, more usefully, what was *measured* — read it before assuming a shape is arbitrary. The ADRs in [`docs/adr/`](docs/adr/) predate the migration and **still govern**: they hold the reasons, and reasons do not change language. Where an ADR names a Python path, read it as naming the decision, not the file.

## Der Umstieg auf Noelia ist gefahren (ADR-0045)

**Eine Versionsnummer hinter „Girder" ist Geschichte, kein Verweis.** „Girder
4.3.0 hatte einen Substring-Fehler in seiner Maskierung" bleibt stehen: eine
Messung an einer veröffentlichten Version umzubenennen hieße, sie zu fälschen.
Wo unten eine Messung „Girder 4.4.0" nennt, ist sie vor dem 20.09.2026
entstanden.

Der Auftrag sah zwölf Sprünge vor, weil **5.0 eine Datenwanderung ist** —
Sitzungen, Widerrufe, der Passwort-Pfeffer, die Prüfspur. **Gefahren wurde ein
Schnitt**, und die Bedingung dafür steht in ADR-0045: diese Plattform ist nicht
in Betrieb, also hat die Wanderung keinen Gegenstand. Datenträger geleert, keine
Kompatibilitätsschicht, kein `[Obsolete]`, kein zweiter Pfad daneben.

**Der Umstieg kostete drei API-Änderungen über zwölf Versionen**, jede
dokumentiert: `ISovereignAuditSink` nach `Noelia.Abstractions.Audit` (5.2.0),
`ITokenSessionService` nach `Noelia.Abstractions.Security.Sessions` (5.0.0),
und `SessionObservations` als fünfter Konstruktorparameter von
`TokenSessionService` (5.1.0) — letzteres, weil die beobachteten Subjekte ein
Instanzfeld an einem `AddScoped`-Dienst waren und die Sitzungsübersicht deshalb
dauerhaft „0 active sessions" meldete. **Registriert, vorhanden, ohne Wirkung**
— dieselbe Fehlerklasse, für die ADR-0044 hier gebaut wurde.

## CI is green on the .NET branch as of 09.09.2026

*(aus `### CI`; die geltenden Regeln zur CI stehen im Skill `wt-ci`)*

**CI is green on the .NET branch as of 09.09.2026** — the first time, and the last green run before it was 13.08.2026, in the Python era. What it actually drove, rather than merely reporting a tick: **966** .NET tests (0 red, 0 skipped), **23** Playwright journeys, **414** route-map answers across the three principals, **79** projects scanned for vulnerable packages, plus both shipped images built, the whole compose stack up, and the chart rendered.

Getting there took four fixes, and **not one of them was in the product code**. Every single one was a place where two sides named the same thing differently, and each stayed invisible until something outside this machine ran it. That is the pattern worth keeping:

**Two lines were missing, and between them they took every job down.** All five jobs red:

- **No `permissions:` block at all**, so the run token carried the account default, which does not include `packages`. Every `dotnet restore` answered `NU1301 … 403 (Forbidden)` for each Noelia package in turn — that reads like a broken feed and is a missing line in the workflow. The block now names `contents: read` as well, because an explicit block *replaces* the default rather than adding to it: `packages` alone would take checkout away.
- **`pnpm/action-setup@v4` with no `with:`**, on all three Node jobs. It looks for `packageManager` in the **root** `package.json`, and that file went away when the workspace collapsed into `web/`. Five seconds in: `Error: No pnpm version is specified.` The fix is a pointer (`package_json_file: web/package.json`), never a `version:` here — a second number is the one that stays behind at the next bump.

The pnpm half is **proved**: `frontend-quality` went green on the next run — and with it the open question about **node 25**, which this machine (24) could not answer.

**The `permissions:` block is necessary and not sufficient, and that too is now measured rather than reasoned.** With the block in place the run token still answers `403 (Forbidden)` on every Noelia package. `403` is the informative part: the token authenticated and was *refused*, so the missing thing is not a scope but an **entitlement**. **Noelia is a private package and this repository is public**, and a package grants access per repository — the run token of `workertransfer` has no claim on a package published from `girder` until somebody says so.

There are exactly two ways to say so, and both are account-level actions no workflow can perform:

- **Package settings → *Manage Actions access* → add `workertransfer`.** The better one: nothing to rotate, and the entitlement is visible where the package lives.
- **A `GIRDER_TOKEN` secret** — a PAT with `read:packages`. It hangs on no repository grant, which is why it goes *first* at all four login points, with the run token as the fallback: `${{ secrets.GIRDER_TOKEN || secrets.GITHUB_TOKEN }}`.

The older comment claimed the run token "reiche dafür". That was an assumption, it was never measured, and it is false. **A pull request from a fork cannot build here under either fix** — it gets no secrets and no entitlement to a private dependency.

Measured locally while the workflow was being repaired: both audits are clean (79 projects named, no vulnerable packages; `pnpm audit --prod` finds none), and the frontend gate passes end to end (`tsc` clean, 150 tests in 20 files, `vite build` green). So once the entitlement stands, what remains untested by anything but CI is the .NET half.

**And behind the entitlement stood two more, both of the same family: something was read in a place that spelled it differently.** The `GIRDER_TOKEN` secret cleared the restore — `dependency-audit` went green — and uncovered them.

- **`scripts/test-dotnet.sh` read its own output in German.** It grepped `^(Bestanden!|Fehler!)` and `erfolgreich:`; the runner answers `Passed! - Failed: 0, Passed: 966, Skipped: 0`. All sixteen suites reported *"keine Ausgabe — die Reihe lief gar nicht"* while in fact running and passing — Identity took two minutes and still counted as never started. The guard was right to be loud; it was reading the wrong language. `DOTNET_CLI_UI_LANGUAGE=en` is now exported **in the script**, not in the workflow: whoever reads the output owns its language, or the result depends on who called. The same trap `dependency-audit` already documented, one directory over.
- **The docker secret file was named with `${{ env.HOME }}`, and the `env` context has no `HOME`.** It holds only what an `env:` block declares, so the expression became empty, the path read `/.nuget/NuGet/NuGet.Config`, and BuildKit answered `##[warning]secret file not found` — a *warning*. The build carried on with no credentials at all and died two hundred lines later on `401 (Unauthorized)`. **That is how to tell the two apart: 403 is authenticated-and-refused, 401 is never authenticated.** Both ends now say `${{ runner.temp }}`, and a missing token is named on the spot instead of surfacing as a broken feed — the check reads the *shape*, never the value.

## Die Kommentarregel und ihre Herleitung

*(aus `## Conventions that bite`; die Regel selbst steht kurz in der `CLAUDE.md`)*

- **A comment earns its place when its absence would let someone build a defect back in. Everything else retells the code.** That is the line, decided 12.09.2026 and written up in `docs/AUFTRAG-ENTLASTUNG.md` (Phase 4). Two things follow from it, and both are rules rather than preferences. **First: a reason lives at the line, not in an ADR.** Moving `<remarks>` into ADRs was considered and is *refused*, permanently — every defect of the week before that decision came from a rule that was not where somebody needed it (`secrets` in a step `if`, the egress boundary against a value from the database, the missing `.AsTracking()`, the 404-instead-of-401 door whose rule was already written two lines further down in the same file). A reason in an ADR is a reason you have to *search for*, and nobody searches for what they do not miss. **Second: the mechanical filter is not used again.** It was built and measured three times, and all three times it deleted "why 503 and not 404" — a sentence that passes the test above easily, because without it the next person builds exactly that defect back in. Comments are 31 % of `src/` here; that is a measurement, not a problem to solve, and lowering the number means deleting reasons. What may go is a *copy* of a reason whose canonical home is elsewhere — and that is decided per site, never per pattern: of seven word-identical copies of the `Personenzeile` sentence, three fell and four stayed, because at those four a sibling table in the same file spells the person out as a column and the comment is what stops someone "fixing" the difference.
