---
name: wt-tore
description: Use when running builds, tests, migrations or counter-probes in the WorkerTransfer repository — encodes the six traps that have cost real time (build+test contention, solution-wide dotnet test, missing migrations, missing AsTracking, non-compiling counter-probes, loose E2E selectors).
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

## 4. Ein `SichereAsync` ohne `.AsTracking()` schreibt lautlos nichts

Alle elf Kontexte fahren `QueryTrackingBehavior.NoTracking`. Eine gelesene Zeile
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
