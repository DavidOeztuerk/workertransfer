---
name: wt-tore
description: Use when running builds, tests, migrations or counter-probes in the WorkerTransfer repository — encodes the six traps that have cost real time (build+test contention, solution-wide dotnet test, missing migrations, missing AsTracking, non-compiling counter-probes, loose E2E selectors), plus Docker being off, building images while testing, `--no-incremental` after a revert, a counter-probe that does not fall, and an E2E run against a cold stack.
---

# Die sechs Fallen dieses Repositoriums

Jede davon hat in einer echten Sitzung Zeit gekostet. Sie sehen alle aus wie
etwas anderes, als sie sind — das ist der Grund für diese Fertigkeit.

## 1. `build && test` in einem Aufruf killt die Testcontainers

Und die Fehlschläge sehen aus wie echte Testfehler. **Immer getrennte
Aufrufe.**

## 2. `dotnet test` über die Lösung startet fünfzehn Postgres-Behälter

Der ResourceReaper läuft ab, *alle* Reihen fallen binnen einer Millisekunde mit
`TypeInitializationException` — das sieht aus wie ein kaputter Build und ist
keiner. **Nur `./scripts/test-dotnet.sh`** oder ein einzelnes `.csproj`.

## 3. Eine fehlende Wanderung sieht aus wie 27 kaputte Tests

Nach *jeder* Modelländerung sofort `dotnet ef migrations add`. Sonst fällt die
ganze Reihe in ~18 ms mit `PendingModelChangesWarning`.

## 4. Ein `SaveAsync` ohne `.AsTracking()` schreibt lautlos nichts

Alle vierzehn Kontexte fahren `QueryTrackingBehavior.NoTracking`. Eine gelesene Zeile
kommt **abgelöst** zurück; die Zuweisungen laufen ins Leere, `SaveChanges` sieht
nichts, und der Aufrufer bekommt trotzdem sein `204`.

**Der Anlegepfad verdeckt es**, weil `Add` immer verfolgt — es fällt erst beim
ersten *ändernden* Aufrufer auf, unter Umständen Monate später. Gemessen am
09.09.2026 an `PUT /resumes/me/documents/{id}/as-cv`.

## 5. Eine Gegenprobe muss kompilieren

`else if (false)` ist unerreichbarer Code und damit ein *Build*-Fehler — und ein
Build-Fehler liest sich in der Ausgabe wie ein bestandener Test. Wähle einen
Bruch, der übersetzt.

**Und nach dem Zurücknehmen `--no-incremental` bauen**, nicht nur nach dem
Patch: der inkrementelle Build hat eine Rücknahme dreimal nicht bemerkt.

**Eine Gegenprobe, die nicht fällt, zeigt einen schwachen Test — nicht
richtigen Code.**

## 6. Ein loser E2E-Selektor legt die halbe Reihe still lahm

`getByRole("button", { name: /Speichern/i })` traf ab dem zweiten
Speichern-Knopf zwei Elemente — vierzehn von dreiundzwanzig Reisen fielen, und
niemand merkte es, weil E2E nicht gefahren wurde. Nimm `exact: true`, grenze auf
die Zeile ein (`locator("li").filter(...)`), und prüfe im Zweifel die **Folge**
statt der Beschriftung (nach der Freigabe ist *Senden* bedienbar).

Und: **erst warten, dann klicken.** `click()`/`fill()` haben nur das
actionTimeout (15 s), `expect(...).toBeVisible()` das größere Budget.

---

*Die folgenden Absätze sind am 30.10.2026 wörtlich aus `CLAUDE.md` hierher
verschoben worden („Persistence" und „Conventions that bite"); die verbindliche
Kurzform steht dort weiter.*

## Zu Falle 4: warum der Anlegepfad sie verdeckt (aus `### Persistence`)

**The insert path hides it**, because `Add` always tracks. So it surfaces only at the first *mutating* caller, which may arrive months after the repository was written — `EfUnterlagenSpeicher` (today `EfDocumentRecordStore`) was the only one of nineteen without `AsTracking()`, and had no mutating caller until then. When you write a `SaveAsync`, the read inside it takes `.AsTracking()`; when you review one, that is the line to look for.

## Zu Falle 2: ein Lauf über die Lösung (aus `## Commands`)

**Never `dotnet test` over the solution.** Fifteen suites start fifteen Postgres containers at once, Testcontainers' ResourceReaper times out, and *all* suites fail within a millisecond with `TypeInitializationException` — which looks exactly like a broken build and is not one. `scripts/test-dotnet.sh` runs them one at a time, runs through instead of failing fast, prints how many tests actually ran, and goes red if a suite executed nothing. A green run that silently collected half the suite looks identical to a real one unless the count is on screen.

## Ein Rumpf zwischen Diensten (aus `## Conventions that bite`)

- **A service-to-service body is a typed contract, never an anonymous object.** `new { userId = … }` compiles, serialises, and arrives as `Guid.Empty` at a receiver that declares `[JsonPropertyName("user_id")]`. Measured: all **four** notification hops did exactly this, and the whole notification path had never once delivered — 18 outbox rows given up after ten attempts, and the mailbox held nothing but confirmation mails. The erasure cascade was immune because sender and receiver share `ErasureV1` (then `LoeschungV1`). Where a shared type is genuinely not available, `NotificationWireTests` is the pattern: capture what the sender writes, deserialise it with the *receiver's* type, and assert the value survives — never compare the two names, which is one string checked twice.

## Gegenproben, Docker, kalter Stack (aus `## Conventions that bite`)

- **Run counter-probes, and make sure the break actually compiles** — a build error reads in the output like a passing test.
- **After a counter-probe, build with `--no-incremental`** — and after the *revert*, not only after the patch. The incremental build has failed to notice a revert three times: twice a test fell over code that was already correct, and once a counter-probe *passed* while the rule was genuinely missing. The second case is the dangerous one, because it looks like a licence.
- **A counter-probe that does not fall reveals a weak test, not correct code.** This has happened for the URL scheme check, "validate before write", the unverified-connection guard, the fork filter, the route priorities and the database-creation guard. Every time the answer was to strengthen the test, not to drop the claim.
- **Docker must be running**, or the integration suites skip themselves — and a skipped integration test looks exactly like a passing one. `scripts/test-dotnet.sh` prints the counts for this reason.
- **Never build images and run tests at the same time.** They contend, and the failure looks like a test failure.
- **And a cold stack is the *other* cause with the same symptom.** A freshly started stack is slow before it is warm — JIT, connection pools, the first migration. The first E2E journey against it measures the warm-up and falls at places that have nothing to do with what it asserts. Measured 12.09.2026: `jobs-journey` red on a cold stack, 4/4 green on a warm one — on **both** trees, so independent of the change. **The counter-probe that decides it is not "run it again", it is: run the same suite on the UNCHANGED tree.** If it falls there too, it was never your change; without that step one searches for hours in code that is fine. And **do not raise the timeout** — on 10.09.2026 exactly that hid a real cause (the cache/in-flight race in `kontext.ts`): 5 → 10 s did not help, because it was never too slow. Raising a wait always looks like a fix and just as often conceals one.
