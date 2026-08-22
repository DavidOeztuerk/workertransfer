# Stand der Migration

**Diese Datei wird fortgeschrieben, nicht neu geschrieben.** Wer die Arbeit
aufnimmt, liest sie zuerst und weiß dann, wo es weitergeht — ohne den Verlauf zu
durchsuchen oder sich den Zustand aus dem Repository zusammenzureimen.

Der Auftrag steht in [`MIGRATION-AUFTRAG.md`](MIGRATION-AUFTRAG.md), das
Nachschlagewerk in [`MIGRATION-PROMPT.md`](MIGRATION-PROMPT.md). Hier steht nur,
was davon getan ist.

**Zuletzt fortgeschrieben:** 2026-08-22, beim Start von Welle 1.
**Zweig:** `dotnet-migration`. **Girder:** 3.0.1.

---

## Kurz

| | |
|---|---|
| **Phase A — Fundament** | **fertig**, committet |
| **Phase B — die neun Dienste** | **Welle 1 läuft** (consent · profile · resume) |
| **Phase C — Zusammenbau** | nicht begonnen |
| **Prüfer** | nicht begonnen |
| **Tests** | 211 grün, 0 rot, 0 übersprungen |
| **Offene Girder-Schulden** | keine |

Prüfen lässt sich das mit zwei Aufrufen, **getrennt**:

```bash
dotnet build dotnet/WorkerTransfer.slnx
dotnet test  dotnet/WorkerTransfer.slnx --blame-hang-timeout 300s
```

---

## Phase A — fertig

Vier Stücke, alle committet und grün.

**1. `dotnet/src/shared/WorkerTransfer.Outbox`** — Tabelle je Dienst in dessen
eigener Datenbank (`ConfigureOutbox()`), Absicht in derselben Transaktion
(`IOutbox.VermerkeAsync`), `FOR UPDATE SKIP LOCKED` beim Holen,
`NochNichtException` für „noch nicht" ohne Versuchsverbrauch,
`HoechsteVersuche = null` heißt nie aufgeben, kein Inhalt außer `user_id` und
`kind`. Die Schleife gehört dem Dienst und öffnet je Durchlauf einen Bereich.

**2. `dotnet/src/shared/WorkerTransfer.ServiceDefaults`** —
`AddWorkerTransferDefaults(...)` und `UseWorkerTransferDefaults(...)`.
`AlsAussteller()` ruft **nur identity**. Die RFC-9457-Problemdetails liegen hier.

**3. Die drei Verträge** — `WorkerTransfer.Contracts.Identity` (Tokenform und
was niemals hineingehört), `.Consent` (Frage, Antwort, Sammelfrage,
`HoechsteSammelgroesse = 100`, `Zwischenspeicherdauer = TimeSpan.Zero`),
`.Erasure` (Löschabsicht ohne Begründungsfeld, Quittung, Unternehmensrückzug,
Empfängerliste).

**4. `identity-service` vollständig** — 18 Routen, die Vorlage für alle anderen:

| Bereich | Routen |
|---|---|
| Anmeldung | `POST /auth/{login,refresh,logout,company/{id}}`, `GET /auth/session`, `GET /me` |
| Registrierung | `POST /auth/{register,verify-email,resend-verification}` |
| Unternehmen | `POST /companies`, `GET /me/companies`, Einladungen anlegen/listen/zurücknehmen, Mitglieder listen/entfernen, `POST /invitations/accept` |
| Löschung | `POST /account/erasure` |

Die drei Benachrichtigungsrouten liegen **absichtlich nicht** hier, sondern
gehen an `notification-service`.

---

## Phase B — die neun Dienste

Ein erster Versuch mit neun gleichzeitigen Agenten ist am Ausgabelimit
gestorben, bevor einer eine Datei angelegt hatte; im Repository ist davon nichts
zurückgeblieben. Deshalb drei Wellen zu dritt.

**Welle 1 läuft seit dem Stand dieser Zeile.** Solange sie läuft, ist der
Arbeitsbaum nicht sauber und die Solution kennt die neuen Projekte noch nicht —
das ist erwartet, nicht kaputt.

| Welle | Dienst | Port | Stand | Commit |
|---|---|---|---|---|
| 1 | `consent` | 8002 | **läuft** | — |
| 1 | `profile` | 8003 | **läuft** | — |
| 1 | `resume` | 8004 | **läuft** | — |
| 2 | `portfolio` | 8005 | offen | — |
| 2 | `jobs` | 8006 | offen | — |
| 2 | `applications` | 8007 | offen | — |
| 3 | `companies` | 8008 | offen | — |
| 3 | `transfer` | 8009 | offen | — |
| 3 | `notification` | 8010 | offen | — |

`github-service` fällt ersatzlos weg (ADR-0022) und steht deshalb nicht in
dieser Liste.

### Was nach jeder Welle zu tun ist

1. Die neuen Projekte in `dotnet/WorkerTransfer.slnx` eintragen — **die Agenten
   fassen die Datei nicht an**, damit sie sich nicht gegenseitig überschreiben.
2. Von den Agenten gemeldete Paketversionen in `dotnet/Directory.Packages.props`
   nachtragen — aus demselben Grund.
3. `dotnet build` und `dotnet test` über die ganze Solution, **in getrennten
   Aufrufen**.
4. Diese Datei fortschreiben: Stand je Dienst, Commit, offene Befunde.

### Offene Punkte, die ein Agent gemeldet hat

Noch keine.

---

## Phase C — Zusammenbau

Nicht begonnen. In dieser Reihenfolge:

1. Gateway mit Ocelot, eine Route je Dienst, `Sec-Fetch-Dest: document` trennt
   Seite von Ressource.
2. Compose und Helm auf die .NET-Dienste.
3. Python restlos entfernen, danach `dotnet/` flach in die Wurzel — als eigener
   Commit, damit die Umbenennungen lesbar bleiben.
4. Übergangsgerüst löschen: Ü-1 bis Ü-7 in
   [`uebergang-python-dotnet.md`](uebergang-python-dotnet.md), ersatzlos. Die
   Enum-Guards werden schlichte `CREATE TYPE`.
5. CI neu — und sie baut diesmal die Images.
6. Der Prüfer, gegen die Vision statt gegen Python, mit `CLAUDE.md`-Neufassung.

---

## Was beim Weiterarbeiten immer gilt

- **Bauen und Testen in getrennten Aufrufen.** Verkettet scheitern die
  Testcontainers-Reihen und sehen dabei aus wie echte Testfehler.
- **Docker muss laufen**, sonst fallen die Integrationstests aus — und ein
  ausgefallener Integrationstest sieht aus wie ein bestandener.
- **Kein Umweg um einen Girder-Fehler.** Code richtig schreiben, Test rot
  lassen, Ticket nach `bugs/` mit Reproduktion ohne WorkerTransfer-Code,
  weitermachen.
- **Gegenproben fahren**, und darauf achten, dass der Bruch **übersetzt**: ein
  Build-Fehler sieht in der Ausgabe aus wie ein bestandener Test.
