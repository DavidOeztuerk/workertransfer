# Migration WorkerTransfer: Python → .NET auf Girder

Dieses Dokument ist der Einstieg für eine neue Session. Es ersetzt den Verlauf
der Session, in der Girder gebaut wurde. Lies es ganz, bevor du etwas anfasst.

---

## Was du vor dir hast

Zwei Repositorien auf derselben Maschine:

| | Pfad | Zustand |
|---|---|---|
| **WorkerTransfer** | `/Users/davidozturk/Projects/workertransfer` | Python, produktiv gedacht, zehn Dienste + React-App |
| **Girder** | `/Users/davidozturk/Projects/Girder` | .NET 10, fertig, neun NuGet-Pakete |
| *Skillswap* | `/Users/davidozturk/Projects/Skillswap` | **NUR LESEN.** Nie ändern. Herkunft von Girder. |

Girder liegt auf `github.com/DavidOeztuerk/girder`, wird nach GitHub Packages
veröffentlicht und ist die Grundlage der Migration.

**Lies zuerst:**

1. `/Users/davidozturk/Projects/workertransfer/CLAUDE.md` — die Projektregeln.
   Sie sind lang und jeder Absatz hat einen Grund. Besonders die Abschnitte zu
   Einwilligung, Löschung und Mandantenfähigkeit.
2. `/Users/davidozturk/Projects/Girder/README.md` — was Girder anbietet, mit
   Beispielen für jeden Anwendungsfall.
3. `/Users/davidozturk/Projects/Girder/docs/adr/0001-souveraenitaet-durch-portschnitt.md`
   — warum Girder so geschnitten ist. Der Abschnitt „Was die Umsetzung
   korrigiert hat" nennt Fehler, die du nicht wiederholen sollst.
4. `/Users/davidozturk/Projects/workertransfer/docs/ULTRAPLAN.md` und
   `docs/ROADMAP.md` — Stand des Python-Systems.

---

## Der Umfang, gemessen

```
identity-service      5.562 Zeilen Python   8 Migrationen
transfer-service      2.634                 5
resume-service        2.082                 4
consent-service       1.833                 1
applications-service  1.699                 2
jobs-service          1.630                 2
profile-service       1.528                 1
github-service        1.317                 1
portfolio-service     1.276                 1
companies-service     1.036                 2
                     ─────
                     20.597 Zeilen, 27 Migrationen, 203 Testdateien, 31 ADRs
```

Dazu 19 Python-Pakete unter `packages/` und eine React-App unter `apps/web`.

**Die React-App wird nicht migriert.** Sie spricht HTTP; solange die Verträge
gleich bleiben, merkt sie vom Wechsel nichts. Das ist auch der Prüfstein für
jeden migrierten Dienst.

---

## Die eiserne Regel

> **Der Benutzer hat gesagt: keine Skripte für die Migration. Handübersetzung.**
> *„das würde nur vieles kaputt machen"*

Das gilt. Du darfst Skripte für *Recherche* benutzen (grep, Messungen,
Inventare), aber der übersetzte Code entsteht von Hand, Datei für Datei, mit
Verständnis für das, was dort steht.

Der Grund ist in diesem Projekt belegbar: In `CLAUDE.md` stehen Dutzende
Entscheidungen, die im Code aussehen wie Nachlässigkeit und keine sind — `404`
statt `403` für ein verborgenes Profil, `GRANTED` das nicht „gilt jetzt"
bedeutet, eine Löschung ohne Begründungsfeld. Eine mechanische Übersetzung
zerstört genau diese Stellen, weil sie sie nicht erkennt.

---

## Was du übernimmst und was nicht

### Übernimm aus Girder

- Identität und Mandantenfähigkeit (`Capacity`, `Principal`, Tenant-Filter)
- Berechtigungen, Ressourcenrechte, bedingte Berechtigungen
- Token-Widerruf samt Degradation
- Ratenbegrenzung, Cache, Geheimnisse, Verschlüsselung, Prüfspur
- Middleware-Pipeline, Gesundheitsprüfungen, Telemetrie, Resilience
- Egress-Politik und Souveränitätsbericht

### Baue neu, weil Girder es bewusst nicht hat

- **Einwilligung.** Girders Compliance-Bereich wurde gelöscht (3.002 Zeilen),
  weil er Domäne ist. `apps/consent-service` ist die maßgebliche Fassung — mit
  ADR-0013 (synchron gelesen, nie zwischengespeichert) und ADR-0030
  (`/check-batch`). Übersetze **den**, nicht Girders gelöschte Variante.
- **Löschung/Erasure.** ADR-0027. Die Kaskade, der Outbox-Weg, die sieben
  Empfänger, `RETAIN_*` als Konstante statt Einstellung.
- **Alle Domänenmodelle.** Profil, Lebenslauf, Portfolio, Stellen, Bewerbungen,
  Transfers, Unternehmen.
- **Hintergrundaufgaben.** Girder hat zehn Schleifen gelöscht, weil Aufräum-
  *Politik* dem Dienst gehört. Was `transfer-service` und `identity-service`
  brauchen, gehört in ihre eigene Infrastrukturschicht.

---

## Vorgeschlagene Reihenfolge

**Nicht mit identity-service anfangen**, obwohl es die Referenz ist. Es ist mit
5.562 Zeilen das größte und hat die meisten Sonderfälle. Ein Fehler im
Vorgehen kostet dort am meisten.

| # | Dienst | Warum diese Stelle |
|---|---|---|
| 0 | **Gerüst + ein Dienst als Muster** | `companies-service` (1.036 Zeilen), einfachste Domäne, benutzt aber Mandantenfähigkeit — beweist das Girder-Fundament |
| 1 | `consent-service` | Alles andere hängt daran. Muss vor Profil/Lebenslauf/Portfolio stehen |
| 2 | `profile-service` | Erster Konsument des Ledgers, klärt das 404/403/503-Muster |
| 3 | `jobs-service` | Caching-Regeln (ADR-0031), Skill-Vokabular |
| 4 | `resume-service`, `portfolio-service` | Dasselbe Muster, strenger |
| 5 | `applications-service` | |
| 6 | `transfer-service` | Outbox, ADR-0025 |
| 7 | **`identity-service`** | Zuletzt, mit allen gelernten Mustern |
| 8 | `github-service` | ADR-0022 beachten: es bewertete Menschen |

**Nach jedem Dienst:** die React-App muss unverändert gegen ihn laufen. Wenn
sie es nicht tut, hat sich ein Vertrag geändert, und das ist ein Fehler, kein
Fortschritt.

---

## Schritt 0 im Detail

Bevor irgendein Dienst übersetzt wird:

1. **Solution anlegen.** `WorkerTransfer.slnx`, `.NET 10`, Central Package
   Management, dieselbe Struktur wie Girder (`src/`, `tests/`).
2. **Girder einbinden.** Erst als `ProjectReference` auf das lokale Repo — die
   Ports werden sich beim ersten echten Konsumenten noch bewegen. Auf
   `PackageReference` umstellen, sobald ein Dienst produktiv läuft.
3. **Einen Dienst vollständig bauen**, mit Tests, Migrationen und laufender
   React-App dagegen. Erst wenn das steht, ist das Muster bewiesen.

**Die Pro-Dienst-Struktur** (aus Girders README und dem workertransfer-ADR-0003
— Composition Root pro Dienst, kein fluent PlatformBuilder):

```
src/WorkerTransfer.Companies/
  Domain/          Entitäten, Wertobjekte, Domänenereignisse
  Application/     Commands, Queries, Handler, Ports
  Infrastructure/  DbContext, Repositories, Migrationen
  Api/             Endpunkte, Composition Root
tests/WorkerTransfer.Companies.Tests/
```

---

## Fallen, die dieses Projekt schon kennt

Aus `CLAUDE.md` und den 31 ADRs. Jede hat Geld oder Vertrauen gekostet:

- **Mandant ist ein Unternehmen; eine natürliche Person hat keinen.** ADR-0017.
  Girders `Capacity` bildet genau das ab — `AsSelf` vs. `ForCompany`, kein
  nullbares `TenantId`.
- **Einwilligung wird synchron gelesen und nie zwischengespeichert.** ADR-0013.
  Ein Widerruf muss beim nächsten Lesen wirken. Kein `CachingBehavior` auf
  irgendetwas, das von einer Einwilligungsprüfung abhängt.
- **`404` heißt „verborgen oder nicht vorhanden", und beide Antworten müssen
  byte-gleich sein.** Wer das „aufräumt", baut einen Aufzählungskanal.
- **`GRANTED` heißt „wurde einmal gewährt", nicht „gilt jetzt."** Deshalb hat
  `ResumeRequest` weder `is_active` noch `revoked_at`.
- **Die Löschung hat kein Begründungsfeld.** Von jemandem, der gehen will, eine
  Rechtfertigung zu verlangen, ist ein Hebel gegen ihn.
- **Aggregate kommen losgelöst aus den Repositories.** Eine Änderung erreicht
  die Datenbank nur über ein ausdrückliches `save()`. In .NET mit EF Core ist
  das anders (Change Tracking) — **das ist eine echte Verhaltensänderung und
  muss bewusst entschieden werden.**
- **Skill-Vokabular benennt um, es folgert nie.** ADR-0023. `"Postgres" ==
  "PostgreSQL"` ist erlaubt, `"React impliziert JavaScript"` nicht.
- **Passung wird im Browser berechnet und existiert sonst nirgends.**
  Kein serverseitiges Matching, kein Score, keine Rangliste von Menschen.

---

## Wie du arbeiten sollst

Der Benutzer hat im Verlauf mehrfach dasselbe eingefordert, und es hat jedes
Mal echte Fehler gefunden:

1. **Tests zuerst festlegen, dann den Code anpassen.** Nicht den Test
   umschreiben, bis er grün wird. Wenn ein Test fällt, ist erst zu klären, ob
   der Code falsch ist.
2. **Gegenprobe fahren.** Ein Test, der auch bei kaputtem Code grün bleibt,
   beweist nichts. In der Girder-Session waren zwei meiner eigenen Tests
   wirkungslos, und beide sahen richtig aus.
3. **Nichts blind übernehmen.** Girder entstand aus Skillswap, und ein großer
   Teil der Arbeit bestand darin, Übernommenes wieder zu löschen: zehn
   Hintergrundschleifen, Compliance, Backup, tote Codepfade. Frage bei jeder
   Datei, ob sie an dieser Ebene richtig ist.
4. **Kommentare sagen was und wie, nicht warum.** Entscheidungen gehören in
   ADRs. Keine Historie im Code („früher stand hier…").
5. **Plan vorlegen und Zustimmung holen, bevor gelöscht wird.**
6. **Ehrlich berichten.** Wenn Tests fallen, sag es mit Ausgabe. Wenn etwas
   übersprungen wurde, sag das auch.

---

## Erster Auftrag für die neue Session

> Lies `CLAUDE.md`, Girders `README.md` und ADR-0001. Miss dann den
> Ist-Zustand von `apps/companies-service` — Endpunkte, Modelle, Migrationen,
> Tests — und leg mir einen Plan für Schritt 0 vor: Solution-Gerüst plus
> `companies-service` als .NET-Dienst auf Girder, mit der Frage nach dem
> Change-Tracking-Unterschied ausdrücklich beantwortet. Erst nach meiner
> Zustimmung anfangen.
