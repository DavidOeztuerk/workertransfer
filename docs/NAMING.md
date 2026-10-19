# Naming — the German→English vocabulary

Code in this repository is written in English (CLAUDE.md, *Conventions that
bite*). This file is the vocabulary the rename follows, so the same German word
does not become `Check` in one service and `Validation` in the next.

**What does NOT move.** The wire and the database were always English and stay
byte-identical: every `[JsonPropertyName]`, every `HasColumnName`, every
`ToTable`, and the migration ids. A rename that touches one of those is wrong —
it is a contract change wearing a refactor's clothes.

**What stays German.** The text a person reads (`web/src/core/i18n/kataloge/de.ts`
— that is the product's language) and prose documents like CLAUDE.md, the ADRs
and the rest of `docs/`. A few strings in code stay German on purpose, and each
says why at its line: the prompts sent to a model, the check ids
(`wt.loeschung.nachweis`), the exemption reasons and finding texts that land in
the German evidence documents, and the keys of `docs/routenkarte.yml`. Wire
words such as `bis_10` or `zeugnis` are the wire, and do not move either.

**Where it stands.** The backend follows this vocabulary since 25.09.2026 —
`src/`, `tests/`, `scripts/`, the Makefile, compose, the chart and the
configuration keys (`10e0157` … `a4ec721`; every commit body lists its renames).
`web/` has not been renamed yet.

## The stems

Most names in this tree are compounds of some forty stems. Translate the stem,
keep the compound's shape.

| German | English | note |
|---|---|---|
| Dienst | Service | `Dienstgrundlage` → `ServiceFoundation` |
| Grundlage | Foundation | |
| Speicher | Store | repository implementations: `EfProfilspeicher` → `EfProfileStore` |
| Zeile | Row | EF row types: `MandatZeile` → `MandateRow` |
| Pruefung | Check | a runnable check (`ISecurityCheck`) |
| …Pruefung (FluentValidation) | Validator | `AnmeldenPruefung` → `SignInValidator` |
| Pruefspur | AuditTrail | never `AuditLog` — ours is a table, not telemetry |
| Geheimnis | Secret | |
| Geheimnisspeicher | SecretStore | |
| Schluessel | Key | |
| Schluesselbund | KeyRing | |
| Nachweis | Evidence | never `Proof`, never `Compliance` |
| Beleg | Evidence | in the singular sense: one piece |
| Einwilligung | Consent | |
| Freigabe | Release | the act; `Sichtbarkeit` is the state |
| Widerruf | Revocation | |
| Loeschung | Erasure | never `Deletion` — ADR-0027 means the Art. 17 sense |
| Loeschempfaenger | ErasureRecipient | |
| Wanderung | SchemaMigration | not `Migration`: EF owns that name |
| Bereitschaft | Readiness | |
| Aufbewahrung | Retention | |
| Bewerbung | Application | the aggregate is `JobApplication` — `…Applications.Application` is the layer |
| Stelle | Job | the advert; `Position` is the role |
| Profil | PersonProfile | the aggregate; `Profile` is the service |
| Lebenslauf | CurriculumVitae | the aggregate; `Resume` is the service |
| Unterlage | DocumentRecord | one stored document in resume-service |
| Entwurf | Draft | |
| Entwerfer | Drafter | the port (`IEntwerfer` → `IDrafter`) |
| Anschreiber | CoverLetterWriter | |
| Befehl | Command | `…Befehl` → `…Command`; the marker is `ICommandMessage`, not Noelia's `ICommand<T>` |
| Abfrage | Query | the marker is `IQueryMessage` |
| Versand | Dispatch | `VersandBehavior` → `DispatchBehavior` |
| Gespraech | Conversation | |
| Mandat | Mandate | |
| Vorgang | Case | a running process; `Transfer` only where it is one. Locals are `caseItem` — `case` is a keyword |
| Arbeitsprobe | WorkSample | |
| Einreichung | Submission | |
| Bewertung | Evaluation | never `Rating`, never `Score` (ADR-0022) |
| Erreichbarkeit | Availability | |
| Marktstatus | MarketStatus | |
| Anbieter | Provider | |
| Herkunft | Origin | |
| Verbindung | Connection | |
| Mitgliedschaft | Membership | |
| Einladung | Invitation | |
| Sitzung | Session | |
| Meldung / Benachrichtigung | Notification | |
| Postfach | Inbox | |
| Ablage | DocumentStore | bytes for certificates (ADR-0035) |
| Personenzeile | SubjectKeyedTable | the EF annotation; the erasure guard reads it |
| Faehigkeit | Skill | |
| Wortschatz | Vocabulary | |
| Wortfund | TermFinder | |
| Fund | Term | what a document yielded |
| Berufsfeld | OccupationalField | |
| Anschrift | PostalAddress | never `Address` alone — that is also a URL here |
| Adresse (a URL) | BaseUrl | settings: `Consent__Adresse` → `Consent__BaseUrl`; `Adressen` → `BaseUrls` |
| Zeitueberschreitung, Geduld | Timeout | |
| Rueckadresse | RedirectUrl | the OAuth callback |
| Takt | PollInterval | the outbox loop |
| Auskunft | Lookup | `Auskunft__Resume` → `Lookup__Resume`; ports `ICompanyLookup`, `IPersonLookup` |
| Anfrage | ResumeRequest / MarketRequest | never bare `Request`: MediatR already names its handler parameter that, and a local `request` beside it does not compile. Locals use `requestRecord`. |
| Antwort | Response | |
| Fassung | Version | |
| Stand | State | a status value |
| Gestalt | Shape | `Ablehnungsgestalt` → `RejectionShape` |
| Tor | Gate | `Einwilligungstor` → `ConsentGate` |
| Naht | Seam | the AI seam (ADR-0024) |
| Grenze | Boundary | the egress boundary |
| Karte | Map | `Routenkarte` → `RouteMap` |
| Bremse | Brake | deleted, but the word appears in prose |
| Klammer | Scope | `Transaktionsklammer` → `TransactionScope` |
| Zeiger | Cursor | `Seitenzeiger` → `PageCursor` |
| Laenge | Length | `Seitenlaenge` → `PageLength` — never `Count` (ADR-0026) |
| Umgebung | EnvironmentFile | not `Environment`: it would shadow `System.Environment` |
| Reise | Journey | test suites: `ProfilreiseTests` → `ProfileJourneyTests` |
| Auflagen | Conditions | `AuflagenTests` → `ConditionsTests`, one per service that has conditions |
| Ganzes | CrossCutting | the suite `WorkerTransfer.CrossCutting.Tests` |
| Probe | Probe | test doubles: `Probetor` → `ProbeGate` |
| Tokenform | TokenShape | |
| Stufe | Stage | advisor; on the wire still `1`/`2`/`3` |
| Haken | Tick | scout: `HakenV1` → `TickV1` |
| Treffer | Hit | scout: `HitV1` |
| Wortausnahme | TermExemption | the reason stays German — it lands in the evidence document |
| Remotegrad | RemoteMode | |
| Umkreis | SearchRadius | |
| Kanarienvogel | Canary | |
| Gegenprobe | CounterProbe | |
| Ortskunde | PlaceLookup | resolves free text to coordinates (ADR-0032) |
| Rolle | Role | |
| Recht | Permission | `Adminrecht` → `AdminPermission` |

## Two names that are decisions, not translations

- **`Seitenlaenge` → `PageLength`, never `PageCount` or `Total`.** ADR-0026: the
  difference between a total and the page length would say how many profiles are
  *not* released. The name is the guard; `Adr0022Tests` forbids the others.
- **`Bewertung` → `Evaluation`, never `Score` or `Rating`.** ADR-0042 holds
  exactly one evaluation per case, as text. The forbidden vocabulary is scanned
  for, so a careless `Rating` fails a test rather than a review.

## Test method names

German sentences become English ones, keeping the underscore style:

```
Ein_leeres_Loeschgeheimnis_schliesst_den_Endpunkt
  → An_empty_erasure_secret_closes_the_endpoint
```

## Six places where an identifier is also a contract

A green build proves nothing about these. Five were measured on 21.09.2026 and
the sixth on 22.09.2026; each one surfaced only at runtime — or, in the last two
cases, as a schema change and as a 500.

- **Anonymous objects.** `new { dienst = …, pfad = … }` becomes JSON, and its
  keys carry no quotes, so masking string literals does not protect them. The
  gateway harness lost 43 of 89 journeys to this; the reading side looked the
  key up as a string.
- **Route templates.** `"/{stelle:guid}/applications"` is a string, its lambda
  parameter is not. After the rename ASP.NET could not bind and answered **400
  where the test expected 403** — which reads like a domain fault.
- **Raw string literals** (`"""…"""`). A `@"…"` pattern does not match them, so
  the SQL inside was renamed (`@typ` → `@type`) while the `AddWithValue("typ")`
  beside it was not.
- **`case` and `default` are C# keywords.** `vorgang` and `vorgabe` translate
  straight into them and stop compiling; they are `caseItem` and `defaultValue`.
- **Postgres enum labels follow the C# member name.** Npgsql derives them, so
  `Lebenslauf` → `CurriculumVitae` renames `lebenslauf` to `curriculum_vitae` in
  the database — and PostgreSQL cannot replace a label rows still point at. The
  answer is not a hand-written `ALTER TYPE`: delete the migrations and let
  `dotnet ef migrations add` generate them, so a fresh schema creates the right
  label from the start.
- **A settings property name IS the environment key.** `ConsentSettings.BaseUrl`
  binds `Consent__BaseUrl`, and that key stands in `docker-compose.yml`, in the
  chart and in `.env.example`. Renaming the property alone leaves the value
  unbound at its default — measured while the keys were still German:
  `Storage__Wurzel` → `Root` made `POST /portfolios/me/attachments` answer
  **500**, and five of twenty portfolio journeys fell. Some of those keys sit in
  the shared compose anchor (`Erasure__Secret`), so **one service cannot move
  alone**. They were therefore renamed in a single pass over the whole tree —
  code, compose, chart, `.env.example`, scripts — on 25.09.2026 (`229de78`), with
  no German alias left beside a key. Whoever runs an older `.env` renames its
  variables by hand (`229de78` lists them): compose aborts on the one required
  variable that moved, but the optional ones fall back silently — an old
  `WORKERTRANSFER_BREMSE_FAKTOR=40` leaves the brake at factor 1. `ComposeKeyTests` now reads
  `docker-compose.yml` and requires every key a service's block sets to land on a
  settings property of that service, and the old German keys to land nowhere.
  The same trap stands wherever a name is bound by convention: `RouteMapTests`
  binds the German keys of `docs/routenkarte.yml` with `[YamlMember(Alias = …)]`,
  and without the alias every column falls silently to 0.

## How a rename is verified

`dotnet build` at 0 errors and 0 warnings, then `./scripts/test-dotnet.sh` in
its own invocation — never chained, or the Testcontainers suites die and look
like real failures. For `web/`, `pnpm check`, then `pnpm test`, then `pnpm build`.

**The build is not the check.** It misses every one of the places above, and it
missed all of them on 21.09.2026: the suite found them, at runtime, after a green
build. A rename is finished when the suite is green, not when it compiles.

**And build the SOLUTION, not the project.** A per-project build is green while
another suite still names the old type — measured 22.09.2026: renaming
`Wortschatz.Kanonisch` and `Portfolio.Domain.Ablage` left six errors in three
foreign test projects that `dotnet build <that>.csproj` could not see.
