# Auftrag: Umstieg von Girder 4.4.0 auf Noelia 6.4.0

**Stand:** 20.09.2026 · **Zweig:** ab `develop`, **nach** dem Zusammenführen von PR #87
**Nächstes ADR:** 0045 · **Vorgänger:** `AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md`

---

## 0. Warum dieser Auftrag hinterherkommt, und was das gekostet hat

Der Vorgängerauftrag hat in seinem zweiten Satz entschieden, **keinen Quelltext zu
übertragen**, weil „WorkerTransfer auf Girder steht, nicht auf Noelia". Diese
Entscheidung war falsch, sie stand nicht zur Disposition und sie hätte eine Frage
sein müssen.

Denn Girder und Noelia sind **dieselbe Codelinie**.
`MIGRATION.md:781` im Noelia-Baum trägt die Überschrift
*„4.4.3 code line → Noelia 5.0.0"*. Der Umstieg ist kein Portieren auf eine
fremde Bibliothek — es sind **zwölf dokumentierte Sprünge** auf derselben Linie:

```
4.4.0 → 4.4.1 → 4.4.2 → 4.4.3 → 5.0.0 → 5.1.0 → 5.2.0 → 5.3.0
      → 6.0.0 → 6.1.0 → 6.2.0 → 6.3.0 → 6.4.0
```

Jeder dieser Sprünge hat einen Abschnitt in `MIGRATION.md`. Es gibt keinen
Schritt, den jemand erfinden muss.

**Was die falsche Reihenfolge gekostet hat.** `src/shared/WorkerTransfer.Nachweis/`
ist am 20.09.2026 gebaut worden — sieben Prüfungen, ein Läufer, sieben Adressen
je Dienst, ein signiertes Dokument. Noelia 6.4.0 liefert davon: `ISecurityCheck`
samt Läufer mit Zeitgrenze und Wertfreiheit, `Noelia.Dashboard` mit genau diesen
Abschnittsseiten und ihrem 404-Verhalten, `OperatorReport` als
`/noelia/report.json`, `RegulatoryReference` mit dem Vier-Felder-Zitat, und die
drei KI-Prüfungen.

Wäre dieser Auftrag zuerst gelaufen, wären die Phasen 3 bis 5 des Vorgängers
*„Noelias Dashboard komponieren und vier eigene Prüfungen schreiben"* gewesen.

**PR #87 wird trotzdem zusammengeführt, nicht verworfen.** Seine *Prüfungen* sind
WorkerTransfers eigene und überleben den Umstieg — sie wechseln die Basisklasse,
nicht ihren Inhalt. Und die drei Funde, die er hervorgebracht hat (die
Teilzeichenketten-Fehlalarme, die Ledger-Fehldeutung, der Gegenversuch, der nicht
fiel) sind bezahlt und behalten ihren Wert. Was kollabiert, ist die *Oberfläche*.

---

## 1. Was die beiden Sicherheitshinweise unterhalb von 4.4.0 hier bedeuten

Noelias README trägt zwei Hinweise, die Versionen bis **einschließlich 4.4.0**
betreffen — also genau die, auf der dieses Repositorium steht:

| Hinweis | Betrifft | Gilt hier? |
|---|---|---|
| **4.4.1** — `AddEncryption` verschlüsselte nicht (`GHSA-276v-hjxx-vrmw`) | `Girder.Redis`, Aufrufer von `AddEncryption()` | **Nein** |
| **4.4.2** — Umschlag-Metadaten waren nicht authentifiziert | dieselbe Naht | **Nein** |

**Nachgesehen, nicht angenommen:** `Girder.Redis` steht nicht in
`Directory.Packages.props`, und `AddEncryption` / `AddRedisEncryption` /
`IDataEncryptionService` kommen im ganzen `src/`-Baum nur in **einem Kommentar**
in `Dienstgrundlage.cs:246` vor, der erklärt, warum das Modul *nicht* komponiert
wird.

Das ist keine Entwarnung für den Umstieg — es ist der Grund, warum er **keine
Notfallaktion** ist. Er kann geplant laufen.

---

## 2. Was WorkerTransfer heute schon von Noelia hat, ohne es zu wissen

`Dienstgrundlage.cs` ruft auf:

```csharp
services.AddGirder(configuration, environment, dienstname, girder => girder
    .UseDefaults()
    .Use(GirderModule.Principal)
    .UseJwt(jwt => jwt.FromSharedSecret())
    .AddSovereignPlatform(souveraen => souveraen …));

app.UseGirder(environment, dienstname);
app.UseGirderPrincipal();
```

Das ist **zeichengleich** Noelias API. `AddGirder` → `AddNoelia`,
`UseGirder` → `UseNoelia`, `GirderModule` → `NoeliaModule`. `UseDefaults()`,
`UseJwt()`, `AddSovereignPlatform()` heißen schon heute so.

**`AddSovereignPlatform` ist der wichtigste Satz dieses Abschnitts.** Das
Zielregister und die Egress-Grenze sind hier bereits komponiert. Der Umstieg legt
also nichts Neues darunter — er bringt auf etwas Vorhandenes die KI-Achse, die
Rechtsbezüge und die Abschnittsseiten.

`363` Dateien nennen `Girder`, davon `287` allein `using Girder.Core.Identity`.
Der Umbenennungsteil ist groß und langweilig; der gefährliche Teil ist ein
anderer.

---

## 3. Der gefährliche Teil: 5.0 ist eine Datenwanderung

`MIGRATION.md` §*„Before the first Noelia process starts"* nennt acht Punkte.
Vier davon sind für dieses Produkt scharf:

### 3.1 Der Einwilligungsledger ist die schärfste Kante

> *„An empty new revocation prefix must never be mistaken for ‚nothing was
> revoked'."*

Bei einem Produkt, dessen Aktivposten der Einwilligungsledger ist, ist das nicht
ein Migrationshinweis unter acht. **Ein Widerruf, der beim Umstieg still nicht
mitkommt, sieht danach aus wie eine erteilte Einwilligung.** ADR-0013 sagt, dass
eine Einwilligung sofort wirkt; ein leeres neues Präfix erfüllt das dem Buchstaben
nach und bricht es dem Sinn nach.

**Auflage:** Vor dem Umschalten wird die Zahl der Widerrufe im alten Präfix
gezählt und nach dem Umschalten im neuen. Weichen sie ab, wird nicht
umgeschaltet. Diese Zahl steht im Abnahmeprotokoll.

### 3.2 Der Pfeffer

> *„Losing the pepper makes existing password hashes unverifiable."*

Der aktive Passwort-Pfeffer muss nach `noelia.passwords.primary`, **bevor** der
Dienst das erste Mal auflöst. Ohne ihn kann sich niemand mehr anmelden, und es
gibt keinen Weg zurück außer einem Zurückspielen.

### 3.3 Sitzungen und Widerrufe

Alle 4.x-Zugangs- und Auffrischungs-Token ablaufen lassen **oder** jeden
Widerrufs- und Sitzungssatz wandern. Nicht beides halb.

### 3.4 Die Prüfspur

Die alte Kette mit 4.4.3 archivieren und prüfen, dann eine neue beginnen. Die
Ableitungsdomäne und die Ereignisidentität haben sich absichtlich geändert —
Geheimtextbytes zu kopieren ist keine Wanderung.

**Das berührt ADR-0027 (Löschung mit Nachweis).** Ein Löschnachweis, der auf eine
Kette zeigt, die es nicht mehr gibt, ist kein Nachweis. Der Umstieg muss sagen,
wie ein vor dem Umschalten ausgestellter Nachweis danach noch geprüft wird — die
Antwort darf „die alte Kette bleibt archiviert und prüfbar" lauten, aber sie muss
dastehen.

### 3.5 Weiteres, mechanisch

- Tabelle → `noelia_refresh_tokens` (eine Wanderung)
- Mandantenfilter → `NoeliaTenant`
- Umgebungsvariablen → `NOELIA_JWT_KID`, `NOELIA_JWT_PRIVATE_KEY`,
  `NOELIA_JWT_PUBLIC_KEY`; Konfigurationspräfix → `Noelia`
- Messnamen → `noelia.*` (Dashboards und Alarme nachziehen)
- Redis-Instanzname, PostgreSQL-Datenbank und -Nutzer → `noelia`

### 3.6 API-Entfernungen aus 5.0, die hier zutreffen könnten

`ISecretManager` ist weg (→ `ISecretProvider`), `AddSecretManagement(…)` nimmt
keine Argumente mehr und verweigert den Start ohne Anbieter,
`SecretVersion.Value` ist weg, `SecretRotationOptions` und
`SecurityAuditOptions` sind entfernt, weil sie nie gelesen wurden.
`ISovereigntyReport`, `IAuditTrailService`, `ITokenSessionService` sind nach
`Noelia.Abstractions` gezogen — Namensraum nachziehen.

---

## 4. Die Phasen

### Phase 0 — Erst am Demo, nie zuerst hier

`MIGRATION.md` sagt es ausdrücklich: *„Run this cutover first in
`~/Projects/Demo`."* Dort steht ein kleinerer Verbraucher derselben Linie.

**Ein grüner Build beweist nichts.** Was bewiesen werden muss: dass ein
gespeicherter Widerruf, eine bestehende Sitzung und ein alter Passwort-Hash den
Identitätswechsel überleben. Bau dort den Beweis, bevor du ihn hier brauchst.

### Phase 1 — Die zwölf Sprünge, einer nach dem anderen

Nicht 4.4.0 → 6.4.0 in einem Satz. **Sprung für Sprung, mit `make build` und
`./scripts/test-dotnet.sh` dazwischen**, und jeder Sprung liest zuerst seinen
Abschnitt in `MIGRATION.md`.

Die Abschnitte 4.4.0→4.4.1→4.4.2→4.4.3 sind klein. Der Sprung 4.4.3→5.0.0 ist
der Identitätswechsel und bekommt einen eigenen Commit, der nichts anderes tut.

Die Umbenennung selbst ist mechanisch und gehört in **einen** Commit, damit der
nächste Leser sie überspringen kann:

```bash
# Erst messen, dann anfassen
grep -rl "Girder" src tests --include='*.cs' | wc -l      # 363
grep -rn "GirderModule\." src tests --include='*.cs' | wc -l
```

**Warnung, gemessen und nicht geraten:** Der `.gitignore`-Fehler aus PR #87
(`nachweis/` ohne führenden Schrägstrich plus `core.ignorecase=true`) hat zwei
Quelldateien aus einem Commit verschluckt, während lokal alles weiterbaute. Nach
einem Umbenennungs-Commit dieser Größe: `git status --ignored` lesen, nicht nur
`git status`.

### Phase 2 — Was 5.x und 6.x mitbringen, und was WorkerTransfer davon nimmt

| Was | Woher | Wofür hier |
|---|---|---|
| `ISecurityCheck` + Läufer | 5.0 | ersetzt WorkerTransfers eigenen Läufer |
| `Noelia.Dashboard` | 5.0 | ersetzt die sieben `/nachweis/…`-Adressen |
| `OperatorReport` / `report.json` | 6.0 | ersetzt `bericht.json` |
| Ketten-Prüfung, `IReadableSovereignAuditSink` | 6.0 | trägt ADR-0027 |
| `DependencyKind.ArtificialIntelligence` | 6.4 | die KI-Achse |
| `noelia.ai.*` — Inventar, Transfer, Aufzeichnung | 6.4 | drei der sieben Prüfungen entfallen |
| `RegulatoryReference` mit `Reader` | 6.4 | ersetzt `Rechtsbezug` |
| `/noelia/obligations` | 6.4 | ersetzt `/nachweis/pflichten` |

**Die KI-Achse ist der Punkt, an dem WorkerTransfer mehr kann als Noelia.**
Noelia erkennt Modell-Endpunkte am Hostnamen und sagt deshalb, das Verzeichnis
sei eine Untergrenze. WorkerTransfer *hält* die `base_url` in `KiZugangV1`. Der
Weg dahin ist `DeclaredDependency` aus den eingetragenen Zugängen — **aggregiert
und gezählt, nie pro Person.** ADR-0026 gilt auch hier.

### Phase 3 — Was von `WorkerTransfer.Nachweis` bleibt

Vier Prüfungen sind WorkerTransfers eigene und haben in Noelia keine
Entsprechung. Sie werden `ISecurityCheck`-Implementierungen und behalten Inhalt,
Tests und Gegenversuche:

- `wt.ki.naht` — die Feldmenge, die das Modell verlässt (ADR-0024 §3)
- `wt.ki.keine-zahl` — der Laufzeit-Zwilling von `Adr0022Tests`, **samt der
  Silbenanfang-Regel und der dreizehn begründeten Ausnahmen**
- `wt.einwilligung.wirkt` — ADR-0013
- `wt.loeschung.nachweis` — ADR-0027

Alles andere kollabiert auf Noelia. **Was gelöscht wird, wird gelöscht** — nicht
danebenstehen gelassen. Zwei Wege zu einer Aussage laufen auseinander, und beim
ersten Mal merkt es niemand.

Die drei aus PR #87 bezahlten Lehren wandern mit in die Kommentare der neuen
Prüfungen: die Teilzeichenkettenfalle, die Zwei-Tabellen-Regel der Prüfspur, und
dass eine Reihe, die das Geheimnis überall setzt, den Zweig „nicht gesetzt" nie
fährt.

### Phase 4 — `noelia analyze` wird nützlich

Heute sagt das Werkzeug über diesen Baum: *„No Noelia package reference found.
No findings."* — geprüft am 20.09.2026. Nach dem Umstieg findet es:

- die eifrige Provider-Registrierung (`AddX` neben `AddNoelia` statt
  `noelia.UseX` darin) — der Fehler, den `Dienstgrundlage.cs` in seinem eigenen
  Kommentar beschreibt
- `AddNoelia` ohne `UseNoelia`
- fehlende Egress-Erklärung, Versionsdivergenz, Klartextgeheimnisse

```bash
dotnet tool install -g Noelia.Cli
noelia analyze .
noelia analyze --url http://localhost:8003/noelia --operator-secret "$SECRET"
```

`noelia analyze --format json` gehört als eigener Schritt in `make validate`.
Exit **0** sauber, **1** Befunde, **2** das Werkzeug selbst kaputt.

### Phase 5 — Die Control Plane sieht diese Flotte

`~/Projects/NoeliaControlPlane` zieht `/noelia/report.json` von jedem Dienst.
Vierzehn Dienste sind eine Flotte, und es gibt drei Stufen.

Das ist der Punkt, an dem aus WorkerTransfer **der Beweis** für die Control Plane
wird: kein Demoprojekt, sondern eine Vermittlungsplattform mit echten Konten,
echten Modellaufrufen und einer Anhang-III-Frage, die jemand beantworten muss.

Die drei Nachweise — Souveränität, Datenschutz, KI — ersetzen das, was PR #87
selbst signiert hat.

> **Die Lizenz ist hier eine echte Frage.** Vierzehn Dienste über der freien
> Stufe (drei). Für den eigenen Gebrauch stellst du dir eine aus; sobald ein
> Kunde das sieht, ist es eine Preisfrage und keine technische.

---

## 5. Was am Ende wahr sein muss

- [ ] Kein `Girder` mehr in `src/`, `tests/`, `Directory.Packages.props`,
      `docker-compose.yml`, `deploy/` — als `grep`, nicht als Eindruck
- [ ] `make build` 0 Warnungen · `./scripts/test-dotnet.sh` alle Reihen grün,
      Zahl auf dem Schirm, **mindestens 1381**
- [ ] **Der Widerrufs-Zählstand vor und nach dem Umschalten ist gleich** — die
      Zahl steht im Protokoll
- [ ] Ein vor dem Umstieg angelegtes Konto meldet sich danach an
- [ ] Ein vor dem Umstieg ausgestellter Löschnachweis ist danach noch prüfbar,
      oder es steht geschrieben, wie er es wird
- [ ] Die alte Prüfspur ist archiviert **und mit 4.4.3 geprüft**, bevor die neue
      beginnt
- [ ] Jeder Dienst antwortet auf `/noelia`; ohne Berechtigung 404, nie 403
- [ ] `/noelia/ai` nennt die eingetragenen Anbieter **gezählt, nie pro Person**
- [ ] Die vier WorkerTransfer-Prüfungen laufen als `ISecurityCheck`, mit ihren
      Gegenversuchen
- [ ] `WorkerTransfer.Nachweis` ist gelöscht, nicht verwaist
- [ ] `noelia analyze` läuft in `make validate` und ist grün
- [ ] Die Control Plane zeigt alle vierzehn Dienste als eine Flotte
- [ ] ADR-0045 begründet den Umstieg und **nennt, was er an ADR-0027 ändert**
- [ ] `CLAUDE.md`, `README.md`, `AGENTS.md`: Girder → Noelia, auch in der Prosa
- [ ] `docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md` trägt oben einen Verweis,
      dass seine Phasen 3–5 hier aufgegangen sind. **Nicht umschreiben** — er ist
      ausgeführt worden und ist Geschichte

---

## 6. Was ausdrücklich nicht passiert

- **Kein Umstieg ohne Phase 0.** Am Demo zuerst, mit dem Beweis über Daten.
- **Kein Umschalten mit halb gewanderten Widerrufen.** Lieber alle Token
  ablaufen lassen, als die Hälfte zu wandern.
- **Kein Verwerfen von PR #87.** Zusammenführen, dann kollabieren lassen.
- **Kein Girder-Rest „bis später".** Zwei Identitäten im selben Baum sind zwei
  Präfixe in denselben Speichern.
- **Keine Zahl über einen Menschen**, auch nicht durch Noelias Werkzeuge.
  ADR-0022 regiert die Bibliothek, nicht umgekehrt.

---

## 7. Die Frage, die dieser Auftrag nicht beantwortet

Warum gibt es zwei Grundlagenbibliotheken desselben Autors?

Nach diesem Umstieg gibt es eine. Ob Girder 4.4.0 dann archiviert wird, ob
`~/Projects/Demo` und andere Verbraucher mitkommen und ob Noelia damit von einer
Bibliothek zu einer Plattform wird, auf der ein zweites Produkt steht — das ist
eine Entscheidung über das Portfolio und keine über dieses Repositorium.

Sie steht hier, weil sie nach diesem Auftrag ansteht und weil dieser Auftrag sie
unvermeidlich macht.
