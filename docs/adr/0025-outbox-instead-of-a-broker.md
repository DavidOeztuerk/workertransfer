# ADR-0025 — Eine Outbox in Postgres statt eines Brokers; `worker-messaging` gelöscht

- **Status:** angenommen
- **Datum:** 2026-08-05
- **Betrifft:** `packages/worker-outbox` (neu), `packages/worker-messaging` (gelöscht),
  `worker-platform` (`background=`), `apps/transfer-service` (erster Konsument),
  seit 9.2 auch `apps/applications-service` und `apps/resume-service`
- **Verwandt:** ADR-0004 (keine gemeinsame Datenbank), ADR-0021 / ADR-0022 / ADR-0024
  (dieselbe Aufräumregel, dreimal vorher angewandt)

## Kontext

Benachrichtigungen liefen als **„feuern und vergessen"**: nach dem Commit ein
HTTP-Aufruf an identity-service, dessen Fehler geschluckt wurde.

Die Begründung dafür war richtig und bleibt es: eine misslungene Mail darf
niemals den Vorgang kippen, der sie ausgelöst hat. Beim Consent-Ledger geht es
um Erlaubnis — im Zweifel nein; hier geht es um Höflichkeit, und einen Widerruf
zurückzurollen, weil eine Mail nicht rausging, wäre grotesk.

Der Preis war aber, dass die Benachrichtigung dann **für immer weg** ist.
identity-service startet gerade neu, das Netz zuckt, jemand deployt — und
niemand erfährt, dass jemand nach ihm gefragt oder seinen Transfer angenommen
hat. Im Protokoll steht eine Warnung, die keiner liest. Für ein Produkt, dessen
ganze Mechanik auf „die Person entscheidet" beruht, ist eine verlorene
Benachrichtigung kein Schönheitsfehler: wer nicht erfährt, dass gefragt wurde,
kann nicht antworten.

Der ULTRAPLAN sieht für Phase 9 `worker-messaging` vor (aio-pika / aiokafka /
nats-py) plus Outbox/Inbox. Beim Nachsehen: **129 Zeilen, fünf schwere
Abhängigkeiten, drei Broker-Umsetzungen, null Konsumenten.** Die einzigen
Verweise waren zwei `[tool.uv.sources]`-Einträge in `worker-health` und
`worker-scheduler` — und die installieren nichts, sie sagen nur, wo ein Name
aufzulösen wäre. Wort für Wort die Lage von `worker-files` (ADR-0021),
`worker-github` (ADR-0022) und `worker-ai` (ADR-0024).

## Entscheidung

**1. Eine transaktionale Outbox, kein Broker.** Die *Absicht* wird in
**derselben Transaktion** wie die fachliche Änderung geschrieben. Geht die
Änderung durch, liegt die Absicht fest; wird sie zurückgerollt, ist sie weg.
Ein Zusteller im Hintergrund darf danach beliebig oft scheitern — der Vorgang
ist längst durch.

Damit bleibt die alte Zusage („darf nichts kippen") unverändert und die Lücke
ist trotzdem zu. Nebenbei entfällt ein HTTP-Aufruf aus dem Anfragepfad, also
auch die Zeitüberschreitung, die eine Antwort verzögern konnte.

**2. `worker-messaging` ist gelöscht.** Ein Postgres, das jeder Dienst ohnehin
betreibt, trägt eine Outbox mit einer Tabelle. Ein Broker wäre ein weiterer
Dienst im Compose, ein weiterer Ausfallpunkt und ein weiteres Betriebsthema —
für eine Zustellrate von einigen Nachrichten pro Minute. `worker-outbox` hat
**eine** Abhängigkeit (SQLAlchemy, überall vorhanden) statt fünf.

**3. Je Dienst eine eigene Tabelle, in seiner eigenen Datenbank.**
`build_outbox_table(Base)` hängt sie an die `Base` des Dienstes, damit sie in
*seinen* Migrationen auftaucht. Es gibt keine gemeinsame Datenbank (ADR-0004),
also auch keine gemeinsame Outbox.

**4. Der Zusteller läuft im Dienst mit**, über ein neues `background=` an
`create_api_app`. Kein eigener Prozess: ein weiteres Deployment, ein weiterer
Gesundheitscheck und ein weiterer Ort zum Vergessen wären ein hoher Preis für
eine Schleife, die eine Tabelle liest.

**5. Die Tabelle trägt keinen Inhalt.** Nur `user_id` und `kind` — dieselben
zwei Angaben, die der HTTP-Aufruf schon vorher schickte. Eine Outbox ist ein
**dauerhafter** Speicher: was hineingerät, steht danach in jedem Backup und
jedem Dump. Ein `payload`-Feld wäre die Einladung, beim nächsten Feature den
Nachrichtentext mitzuschreiben — und Freitext einer Person gehört dorthin so
wenig wie ein Lebenslauf ins Protokoll (`product-scope.md`). Im Fehlerfall wird
nur die **Art** des Fehlers vermerkt, nie die Antwort des Gegenübers.

**6. Mindestens einmal, nicht genau einmal.** Ein Zusteller, der abstürzt,
nachdem er zugestellt, aber bevor er abgehakt hat, stellt erneut zu. „Genau
einmal" bekommt man über eine Dienstgrenze hinweg nicht geschenkt, und der
schlimmste Fall ist hier eine doppelte Mail — deutlich besser als keine.

**7. Aufgeben heißt liegenlassen, nicht löschen.** Nach `MAX_ATTEMPTS` bleibt
die Zeile abfragbar stehen. Eine Zeile, die still verschwindet, wäre genau der
Zustand, den diese Tabelle abschafft.

## Konsequenzen

- Eine Benachrichtigung überlebt einen Neustart des Empfängers. Ein
  Integrationstest hält genau das fest: kaputter Notifier → Zeile bleibt liegen
  → Dienst wieder da → Zustellung, ohne dass jemand eingreift.
- Zustellung ist nicht mehr sofort, sondern innerhalb des Taktes
  (`WORKER_OUTBOX_INTERVAL_SECONDS`, Voreinstellung 5 s). Für eine E-Mail
  belanglos; für etwas, das sofort sein muss, wäre die Outbox das falsche
  Werkzeug.
  **Das ist keine Theorie geblieben.** Eine E2E-Reise wartete 20 Sekunden auf
  die Mail — bemessen für den alten, synchronen Versand. Unter Last (ein Lauf
  brauchte statt 4,6 ganze 48,6 Minuten) fiel sie um, obwohl die Mail nur
  später kam. Der Zeitrahmen steht jetzt auf 60 Sekunden und heißt
  `MAIL_TIMEOUT_MS`, mit dem Grund daneben: die Reisen prüfen „die Mail kommt
  an", nicht „die Mail kommt in 20 Sekunden an". Wer die Plattform von Hand
  testet, sollte dasselbe wissen — eine Benachrichtigung kann bis zu einen Takt
  auf sich warten lassen.
- Tests, die den Notifier synchron beobachteten, prüfen jetzt zwei Schritte:
  Absicht festgehalten, dann zugestellt. Das ist strenger als vorher — der
  Zwischenzustand war früher nicht prüfbar, weil es ihn nicht gab.
- Fünf Abhängigkeiten weniger im Workspace (aio-pika, aiokafka, nats-py,
  msgspec, pamqp).
- **Nachgezogen in 9.2 (06.08.2026):** `applications-service` und
  `resume-service` benutzen denselben Weg. Damit gibt es im System **keinen**
  Pfad mehr, auf dem eine Benachrichtigung stillschweigend verlorengeht.
  Dabei kam heraus, dass beide Dienste **überhaupt keinen Test auf den
  Notifier** hatten — die Zusage „die Person wird benachrichtigt" war dort nie
  geprüft, weder vorher noch nachher. Jetzt hat jeder einen Integrationstest
  mit kaputtem Zusteller, und für `applications-service` ist er gegenbewiesen:
  ohne `record()` wird er rot.
  Die zehn Zeilen Verdrahtung (`outbox_runner`) sind je Dienst **kopiert**, nicht
  geteilt. Ein gemeinsames Paket dafür wäre ein Kopplungspunkt über eine
  Dienstgrenze hinweg, und der Preis wäre höher als der der Kopie
  (ADR-0003/0004) — dieselbe Abwägung wie beim Consent- und Notify-Adapter.
- Sollte je ein Anwendungsfall echtes Broker-Verhalten brauchen (Fan-out an
  viele Abnehmer, Themen, Rückstau über Stunden), ist diese Entscheidung neu zu
  treffen. Sie hält fest, dass es diesen Fall **heute nicht gibt** — nicht,
  dass es ihn nie geben wird.
