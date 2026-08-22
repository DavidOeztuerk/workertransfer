# Der Auftrag: WorkerTransfer vollständig nach .NET

Dies ist der Sitzungsauftrag. `docs/MIGRATION-PROMPT.md` daneben ist das
Nachschlagewerk — Schichten, Girder je Paket, Fallen, Zweigstrategie. Lies beide,
dann fang an. **Ohne Rückfragen.** Alle Entscheidungen, die getroffen werden
mussten, stehen hier.

Am Ende läuft alles auf .NET mit Girder, Python ist restlos weg, und die Vision
steht.

---

## Die Vision, in einem Absatz

**WorkerTransfer ist ein Transfermarkt für Menschen, der Menschen nicht
bewertet.** Bewerbungen, Direktansprache, Arbeitgeberwechsel — aber die Person
entscheidet, wer was sieht. Das ist keine Funktion, das ist die Statik:
Einwilligung wird synchron gelesen und nie zwischengespeichert; ein verborgenes
Profil ist von einem nicht existierenden nicht zu unterscheiden; ein Lebenslauf
geht an *ein* Unternehmen, nicht an den Markt; Löschen heißt löschen; und
niemand bekommt eine Punktzahl, keine Rangliste, keinen Prozentwert. Darüber
liegt **digitale Souveränität**: eigene Infrastruktur, eigene Schlüssel, kein
Fremddienst, der still mitliest.

Jede Entscheidung im Zweifel gegen die Bequemlichkeit und für diesen Absatz.

---

## Was anders läuft als bisher

Bisher wurde Dienst für Dienst migriert, mit Rückfrage an jeder Weggabelung. Das
hört auf. **Ein Durchgang, parallele Agenten, Entscheidungen sind gefallen.**

### Die eiserne Regel für Girder-Fehler — sie hat sich geändert

Bisher galt: Girder-Fehler → anhalten, melden, warten. **Jetzt gilt:**

> **Schreib den Code so, wie er sein muss. Nicht so, wie Girder ihn heute
> durchlässt.**

Wenn ein Agent überzeugt ist, dass Girder sich falsch verhält:

1. **Der Code bleibt richtig.** Kein Umweg, kein `try/catch` drumherum, keine
   ausgelassene Transaktionsklammer, keine abgeschaltete Prüfung.
2. **Der Test bleibt rot** und beschreibt, was gelten *soll*.
3. **Ticket nach `bugs/<was-kaputt-ist>.md`**, Vorlage in `bugs/VORLAGE.md`, mit
   einer Reproduktion **ohne WorkerTransfer-Code** als Code im Ticket.
4. **Weiterarbeiten.** Nicht warten.

So sieht man am Ende auf einen Blick, was Girder schuldet, und wenn diese Schuld
beglichen ist, wird alles auf einmal grün — statt dass jemand zwanzig Umwege
zurückbauen muss, deren Grund niemand mehr kennt.

**Die Fehlerzuordnung bleibt streng.** Girders Pipeline steht in *jedem*
Stapelabzug; sie beweist nichts. Der Test ist: *lässt sich der Fehler ohne
WorkerTransfer-Code auslösen?* Reproduziert er nicht, ist es unserer, und dann
ist es eine gewöhnliche Aufgabe — Test zuerst, dann der Code, kein Ticket.

---

## Entscheidungen, die gefallen sind

Keine davon steht zur Diskussion. Wer sie für falsch hält, schreibt es in den
Bericht am Ende, ändert aber nichts.

| | Entschieden |
|---|---|
| **Dienstzahl** | Die zehn bleiben, **außer `github-service` — der fällt weg** |
| **`github-service`** | Gelöscht. ADR-0022 hat `worker-github` gelöscht, weil es Menschen bewertete; den Dienst mitzunehmen hieße, genau das zu behalten, was der ADR verurteilt. Er fällt auch aus der Löschkaskade — dann sind es sechs Empfänger, nicht sieben |
| **`notification-service`** | **Neu.** Er ist der Empfänger der Benachrichtigungen und der Ort für Messaging. Er ist **nicht** der Ort der Outbox |
| **Outbox** | `dotnet/src/shared/WorkerTransfer.Outbox`, selbst gebaut, **nicht** MassTransit. **Die Tabelle liegt in der Datenbank des schreibenden Dienstes.** `FOR UPDATE SKIP LOCKED` von Anfang an |
| **Mediator** | Ja, `Girder.Application`, in jedem Dienst mit mehr als fünf Handlern |
| **Prüfspur** | Bleibt ein Domänenschreibvorgang in derselben Transaktion. Girders `AuditBehavior` ist Logging und ersetzt sie nicht |
| **Domänenereignisse** | Bleiben im Aggregat, werden beim Speichern eingesammelt. Kein EventBus-Seam — in Python hat er nur No-op-Abnehmer |
| **Rollen** | **Werden erzwungen.** `admin` gegen `member` ist in Python nirgends geprüft; das wird nicht nachgebaut. Gelesen aus der Mitgliedschaftstabelle, **nie** aus einem Anspruch im Token |
| **`POST /companies`** | Bleibt öffentlich. Wer sich privat registriert und später gründet, hat sonst keinen Weg — Einladung setzt Kollegen voraus, die es nicht gibt. Der Satz in `CLAUDE.md` („nirgendwo sonst") wird stattdessen ehrlich gemacht |
| **Bestätigungsmail** | Keine Outbox. Postkorb im Speicher, Versand nach dem Commit, `/auth/resend-verification` ist der Rückweg |
| **Gateway** | Ocelot, routet und prüft nichts |
| **Frontend** | `apps/web` bleibt. Verträge dürfen sich ändern; dann wird die App angepasst |

---

## Der Ablauf: drei Phasen

### Phase A — das Fundament, seriell, machst du selbst

**Kein Agent startet, bevor A steht.** Alles darin ist geteilte Grundlage; wenn
zehn Agenten sie parallel raten, bekommst du zehn Varianten.

1. **`dotnet/src/shared/WorkerTransfer.Outbox`** — Tabelle, „Absicht in
   derselben Transaktion aufschreiben", Zusteller als Hintergrundschleife,
   `Deferred` für „noch nicht" ohne Versuchsverbrauch, kein Versuchslimit für
   die Löschung, `FOR UPDATE SKIP LOCKED`, **kein Inhalt** (nur `user_id` und
   `kind`).
2. **`dotnet/src/shared/WorkerTransfer.ServiceDefaults`** — die eine
   Erweiterungsmethode, die jeder Dienst in seinem Composition Root ruft, und
   die genau nichts entscheidet, was der Dienst entscheiden muss. Schlüssel
   (privat nur bei identity), Pipeline-Reihenfolge, Problemdetails.
3. **Die Verträge zwischen den Diensten**, als Code in
   `dotnet/src/shared/WorkerTransfer.Contracts.*`: die Tokenform, der
   Einwilligungs-Check samt `/check-batch`, die Löschabsicht. Das sind die
   einzigen Berührungspunkte; alles andere ist dienstintern.
4. **Ein Muster-Dienst, vollständig**, an dem die Agenten sich ausrichten.
   `identity-service` ist bereits zu Hälfte gebaut — zieh ihn fertig und mach
   ihn zur Vorlage.

Committe Phase A, bevor du Agenten startest.

### Phase B — die Dienste, parallel, ein Agent je Dienst

Neun Agenten, gleichzeitig. Sie berühren einander nicht: eigener Ordner, eigenes
Schema, eigene Tests.

```
consent · profile · resume · portfolio · jobs
companies · applications · transfer · notification
```

**Warum das parallel geht:** die Dienste reden nur über die Verträge aus Phase A
miteinander, und die stehen. Kein Agent wartet auf einen anderen.

Jeder Agent bekommt den Auftrag unten, mit seinem Dienstnamen eingesetzt.

### Phase C — Zusammenbau und Abnahme

1. **Gateway** mit Ocelot, eine Route je Dienst, `Sec-Fetch-Dest: document`
   trennt Seite von Ressource.
2. **Compose und Helm** auf die .NET-Dienste umstellen.
3. **Python restlos entfernen** — `apps/` außer `web`, `packages/` außer `ui`,
   `pyproject.toml`, `uv.lock`, `Makefile`, `tests/`, `scripts/`, `docker/`,
   `Kon2.txt`, `var/`, `login-before.png`. Danach `dotnet/` flach in die Wurzel
   ziehen, als eigener Commit.
4. **Das Übergangsgerüst löschen**: Ü-1 bis Ü-7 in
   `docs/uebergang-python-dotnet.md`, ersatzlos. Der `verify_aud`-Eingriff, die
   doppelten Ansprüche, der `type`-Anspruch, die nachsichtigen Validatoren, die
   Enum-Guards werden schlichte `CREATE TYPE`.
5. **CI** neu: `dotnet build`, `dotnet test`, `pnpm` fürs Frontend. Und sie baut
   diesmal die Images — eine grüne CI, die keinen Container baut, beweist nichts.
6. **Der Prüfer** läuft zuletzt, siehe unten.

---

## Der Auftrag für jeden Dienst-Agenten

Setz den Dienstnamen ein und gib das wörtlich weiter.

> Du migrierst **`<dienst>`** von Python nach .NET auf Girder 3.0.1. Du arbeitest
> allein in `dotnet/src/<dienst>/` und `dotnet/tests/`; andere Ordner fasst du
> nicht an.
>
> **Lies zuerst:** `docs/MIGRATION-AUFTRAG.md` (dieser Auftrag),
> `docs/MIGRATION-PROMPT.md` (Schichten, Girder je Paket, Fallen), `CLAUDE.md`
> (die Regeln), die ADRs, die deinen Dienst betreffen, und
> `dotnet/src/identity-service/` als Vorlage.
>
> **Python ist Vorlage, nicht Maßstab.** Übernimm die Begründungen, entscheide
> die Formen neu. Miss, was der alte Dienst beantwortet, und entscheide dann, was
> bleibt. Wo du es besser weißt, mach es besser und schreib in die
> Commit-Nachricht, warum.
>
> **Der Schnitt, ohne Abweichung:** ein Projekt je Schicht. Repository-
> Schnittstellen in Domain, Umsetzungen in Infrastructure. Commands, Queries,
> Handler und Dienstmethoden in Application. Contracts **nur** in Api. Die
> gesamte DI-Registrierung in Infrastructure hinter einem
> `Add<Dienst>Infrastructure()`; in Api wird nur diese eine gerufen, plus
> Girder-Modulauswahl und Pipeline.
>
> **Girder vollständig einsetzen**, je Schicht: `Girder.Core` in Domain,
> `Girder.Contracts` in Contracts, `Girder.Abstractions` und `Girder.Application`
> in Application, `Girder.Infrastructure` und `Girder.Data.EntityFrameworkCore`
> in Infrastructure. Was du nicht brauchst, installierst du nicht.
>
> **Tests zuerst, dann der Code.** Nie den Test biegen, bis er grün wird. Und
> **Gegenproben fahren**: brich jede tragende Regel absichtlich und sieh nach, ob
> genau die zugehörigen Tests fallen — der Bruch muss **übersetzen**, sonst
> siehst du einen Build-Fehler und hältst ihn für einen bestandenen Test.
>
> **Integrationstests sind nicht optional.** Zwei echte Fehler in diesem Projekt
> haben sich nur dort gezeigt, weil Attrappen dieselbe Instanz zurückgaben, die
> sie bekommen hatten. Alles, was über mehrere Aggregate oder mehrere Schritte
> geht, braucht einen Test gegen eine echte Datenbank.
>
> **Wenn Girder sich falsch verhält:** schreib den Code trotzdem richtig, lass
> den Test rot, leg ein Ticket in `bugs/` mit einer Reproduktion ohne
> WorkerTransfer-Code, und **mach weiter**. Kein Umweg. Prüf vorher, ob es
> wirklich Girders ist — der Stapelabzug beweist nichts, Girders Pipeline steht
> in jedem.
>
> **Commit je Einheit**, nicht je Dienst: Domänenschicht, dann je eine
> zusammengehörige Routengruppe, dann Infrastruktur. Bedingung: `dotnet build`
> ohne Warnung, `dotnet test` ohne roten Test außer den mit Ticket belegten, ohne
> übersprungenen. **Bauen und Testen in getrennten Aufrufen** — verkettet
> scheitern Testcontainers-Reihen und sehen dabei aus wie echte Testfehler.
>
> **Melde am Ende:** was du anders geschnitten hast und warum, welche Tests rot
> sind und mit welchem Ticket, welche Gegenproben du gefahren hast und was dabei
> fiel.

---

## Der Prüfer

Ein eigener Agent, **zuletzt**, nachdem alle Dienste stehen. Er schreibt keinen
Produktivcode.

> Du prüfst die abgeschlossene Migration und schreibst `CLAUDE.md` neu.
>
> **Prüfe gegen die Vision, nicht gegen Python.** Der Absatz „Die Vision" in
> `docs/MIGRATION-AUFTRAG.md` ist der Maßstab. Für jede Zusage darin suchst du
> die Stelle im Code, die sie hält, **und den Test, der sie festnagelt**. Eine
> Zusage ohne Test ist eine Absichtserklärung.
>
> Sieben Dinge, die erfahrungsgemäß beim Umbau verlorengehen — jede einzeln
> belegen:
>
> 1. Einwilligung wird **synchron gelesen und nirgends zwischengespeichert**.
>    Kein `CachingBehavior`, kein Cache auf einem Ergebnis, in dem eine
>    Einwilligungsprüfung steckt.
> 2. Ein verborgenes Profil und ein nicht existierendes antworten **byte-gleich**
>    bis auf die Korrelations-ID.
> 3. Es gibt **keinen Score, keine Rangliste, keinen Prozentwert** über Menschen.
>    Grep nach `score`, `rank`, `weight`, `level`, `implies`, `match` — jeder
>    Treffer muss sich erklären.
> 4. Die Löschung löscht **vollständig**, hat **kein Begründungsfeld**, ihre
>    Ausnahme ist eine **Konstante** und steht auf `false`, und die Zustellung
>    **kann scheitern**.
> 5. Ein Lebenslauf geht an **ein** Unternehmen. Es gibt keinen öffentlichen
>    Schalter, und `GRANTED` heißt „wurde einmal gewährt", nicht „gilt jetzt".
> 6. **Nur identity-service hält einen privaten Schlüssel.** Prüf jeden
>    Composition Root.
> 7. **Keine Werte in Logs.** Formen, nicht Inhalte.
>
> **Dann:** kein Python mehr im Repo, kein Übergangsgerüst mehr (Ü-1 bis Ü-7),
> keine unnötige Konfigurationsdatei in der Wurzel. `docs/` und die ADRs sagen,
> was gilt, nicht was einmal galt.
>
> **`CLAUDE.md` schreibst du neu**, nicht fort. Sie beschreibt dann ein
> .NET-System auf Girder. Was den Grund für eine Entscheidung nennt, bleibt; was
> Python beschreibt, geht. Die ADR-Verweise bleiben — sie sind das Wertvollste am
> alten System.
>
> **Melde:** jede Zusage aus der Vision mit ihrer Fundstelle und ihrem Test; jede
> ohne Test als Lücke; jeden offenen Girder-Fehler mit Ticketnamen und dem, was
> er blockiert. Beschönige nichts. Ein Bericht, der alles grün meldet, ist
> verdächtig.

---

## Abnahme

Fertig ist es, wenn all das zugleich gilt:

- `dotnet build` ohne Warnung, `dotnet test` ohne übersprungenen Test, und jeder
  rote Test hat ein Ticket in `bugs/`
- `docker compose up --build` bringt Gateway, alle Dienste und `apps/web` hoch
- Kein Python im Repo. Keine Datei in der Wurzel, die nur Python bediente
- `bugs/` enthält genau die offenen Girder-Schulden, jede mit Reproduktion
- Der Bericht des Prüfers liegt vor, mit Fundstelle und Test je Zusage

**Wenn danach die offenen Girder-Fehler behoben sind, muss alles grün werden,
ohne dass jemand Code zurückbaut.** Das ist der Sinn der Regel oben, und daran
misst sich, ob dieser Auftrag ausgeführt wurde.
