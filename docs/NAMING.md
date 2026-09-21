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
and the rest of `docs/`.

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
| Bewerbung | Application | |
| Stelle | Job | the advert; `Position` is the role |
| Entwurf | Draft | |
| Entwerfer | Drafter | the port (`IEntwerfer` → `IDrafter`) |
| Anschreiber | CoverLetterWriter | |
| Gespraech | Conversation | |
| Mandat | Mandate | |
| Vorgang | Case | a running process; `Transfer` only where it is one |
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
| Anfrage | Request | |
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
| Umgebung | Environment | |
| Reise | Journey | test suites: `ProfilreiseTests` → `ProfileJourneyTests` |
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

## How a rename is verified

The compiler is the check: `dotnet build` at 0 errors and 0 warnings, then
`./scripts/test-dotnet.sh` in its own invocation. For `web/`, `pnpm check` then
`pnpm test` then `pnpm build`. Nothing here is a judgement call the build cannot
catch — except the two decisions above, which have tests.
