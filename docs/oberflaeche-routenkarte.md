# Die Routenkarte — Ist und Soll

Stand 12.08.2026. Schnitt E2.5 aus
[der Oberflächen-Spezifikation](superpowers/specs/2026-08-09-oberflaeche-refactor-design.md).

Eigener Schnitt, weil „eigene Routen" nichts Lokales ist: es ändert Router,
Kopfzeilennavigation, Deep-Links, Gateway-Regeln und E2E-Reisen. Über fünf
E3-PRs verteilt würde der Router fünfmal umgeschrieben und die Navigation
driftet.

## Ist: 24 Routen

**Der Stand VOR diesem Schnitt** — die Tabelle bleibt als Beleg stehen, damit die
Umbenennungen weiter unten nachvollziehbar sind. Wer die heutigen Adressen
braucht, liest „Soll — die Karte".

Gemessen aus `apps/web/src/app.tsx`. Es gibt 26 Routendateien, aber
`auth-layout.tsx` ist eine Hülle und keine Route, und `/` bedient **zwei**
Seiten.

| Adresse | Zugang | Was es ist | Zeilen |
|---|---|---|---|
| `/` | öffentlich **oder** angemeldet | **zwei Seiten in einer Adresse**: abgemeldet `home.tsx` (Werbung), angemeldet `overview.tsx` („Was liegt an") | 65 / 189 |
| `/login` | öffentlich | Anmelden | 82 |
| `/register` | öffentlich | Konto erstellen | 121 |
| `/verify` | Token aus der Mail | Adresse bestätigen | 113 |
| `/invitation` | Token aus der Mail | Einladung annehmen | 127 |
| `/jobs` | öffentlich (Bewerben nur angemeldet) | Stellenliste **+ eingebettetes Bewerbungsformular** | 457 |
| `/karriere/$slug` | öffentlich | Karriereseite eines Unternehmens | 142 |
| `/profile` | angemeldet | eigenes Profil | 297 |
| `/resume` | angemeldet | Lebenslauf **+ Anfragenliste** | 290 |
| `/portfolio` | angemeldet | Arbeiten **+ Datei-Upload je Arbeit** | 331 |
| `/github` | angemeldet | GitHub-Belege | 186 |
| `/applications` | angemeldet | eigene Bewerbungen | 146 |
| `/freigaben` | angemeldet | Meine Freigaben | 183 |
| `/meine-daten` | angemeldet | Datenexport | 171 |
| `/einstellungen` | angemeldet | Einstellungen | 131 |
| `/konto-loeschen` | angemeldet | Konto löschen (ADR-0027 §6) | 230 |
| `/markt` | angemeldet | Marktstatus | 290 |
| `/transfers` | angemeldet | eigene Gespräche | 189 |
| `/company/new` | angemeldet | Unternehmen anlegen | 101 |
| `/candidates` | Unternehmen | Kandidatensuche | 418 |
| `/company/jobs` | Unternehmen | eigene Stellen **+ Anlegen/Bearbeiten** | 289 |
| `/company/profile` | Unternehmen | Unternehmensprofil | 183 |
| `/company/team` | Unternehmen | Mannschaft **+ Einladen** | 221 |
| `/company/transfers` | Unternehmen | Transfers des Unternehmens | 232 |

**Wichtig zum Zugang:** „Unternehmen" heißt *nicht*, dass der Client sperrt.
Keine Route prüft `tenant_id === null`. Die Kopfzeile **versteckt** die
Unternehmenseinträge, aber die Adressen sind eingetippt erreichbar — und dann
antwortet der **Server** mit `403` („kein aktives Unternehmen"). Das ist richtig
so: der Server entscheidet, nicht der Browser. Die Karte muss es nur sagen,
damit niemand die Kopfzeile für eine Zugangskontrolle hält.

## Ist: die Navigationsregeln

Heute in `apps/web/src/app.tsx` verstreut, nirgends aufgeschrieben:

| Zustand | Kopfzeile zeigt |
|---|---|
| lädt (`isLoading`) | **nichts** außer „Stellen" — deshalb sah ein Screenshot unter Last anders aus |
| abgemeldet | Stellen · *(auf `/` zusätzlich Prinzipien, Produkt)* · Anmelden · Registrieren |
| angemeldet, ohne Unternehmen | Stellen · Unternehmenswähler · Marktstatus · Gespräche · Menü **Mein Konto** (10 Einträge) · Abmelden |
| angemeldet, mit Unternehmen | dasselbe **plus** Menü **Unternehmen** (5 Einträge) |

„Mein Konto" enthält: Mein Profil, Lebenslauf, Arbeiten, GitHub, Bewerbungen,
Meine Freigaben, Meine Daten, Einstellungen, Konto löschen, **Unternehmen
anlegen**.

Der Unternehmenswähler ist ein rohes `<select>` und erscheint nur, wenn es
Mitgliedschaften gibt. „Ich selbst" ist darin deaktiviert, sobald ein
Unternehmen aktiv ist — zurückwechseln heißt neu anmelden, weil der Tenant im
Token steckt und es keinen Endpunkt gibt, der ihn entfernt.

## Der Fund, der mehr wiegt als er aussieht

**Die Adressen sind halb deutsch, halb englisch** — bei einer durchgängig
deutschen Oberfläche:

| deutsch | englisch |
|---|---|
| `/einstellungen`, `/freigaben`, `/meine-daten`, `/konto-loeschen`, `/markt`, `/karriere/$slug` | `/profile`, `/resume`, `/portfolio`, `/github`, `/jobs`, `/applications`, `/candidates`, `/transfers`, `/company/*`, `/login`, `/register`, `/verify`, `/invitation` |

Das ist nicht nur Kosmetik. **Genau die englischen Adressen kollidieren mit den
API-Präfixen**: `/jobs`, `/applications`, `/transfers` und `/github` sind
gleichzeitig Seite *und* Dienst-Präfix. Deshalb existiert überhaupt die
`Sec-Fetch-Dest: document`-Regel in `docker/traefik/dynamic.yml` — und deshalb
lieferte ein Direktlink auf `/jobs` einmal rohes JSON.

Deutsche Seitenadressen (`/stellen`, `/bewerbungen`, `/gespraeche`,
`/kandidaten`) hätten diese Kollision **gar nicht**. Die Gateway-Regel wäre
dann keine Reparatur mehr, sondern ein Netz, das nichts mehr fangen muss.

Der Preis: jede Umbenennung bricht Lesezeichen und geteilte Links, und sie
berührt die Gateway-Karte, die E2E-Reisen und `jobs/intent.ts` (dort steckt
`/jobs?stelle=<id>`).

## Soll — die Karte

**Alle Adressen englisch, keine Mischung** (entschieden 12.08.2026). Umgesetzt:

| vorher | jetzt |
|---|---|
| `/einstellungen` | `/settings` |
| `/freigaben` | `/consents` |
| `/meine-daten` | `/my-data` |
| `/konto-loeschen` | `/delete-account` |
| `/markt` | `/market` |
| `/karriere/$slug` | `/careers/$slug` |
| `/` (zwei Seiten) | `/` Werbung · **`/overview`** Übersicht |

**Das kostet am Gateway nichts, und das ist gemessen.** Die Regel heißt
`HeaderRegexp("Sec-Fetch-Dest", "^document$")` mit `priority: 200` und enthält
**keinen Pfad** — sie fängt jede Navigation der obersten Ebene, egal wie die
Seite heißt. Die API-Ausnahmen liegen bei `priority: 100`. Deshalb ist auch die
**neue** Kollision abgedeckt, die diese Umbenennung erzeugt: `/market` ist jetzt
gleichzeitig Seite und API-Präfix von transfer-service
(`PathPrefix("/market") || PathPrefix("/transfers")`). Dasselbe galt vorher schon
für `/jobs`, `/applications`, `/transfers` und `/github`.

**Geprüft am laufenden Gateway (12.08.2026), nicht abgeleitet.** Mit
`Sec-Fetch-Dest: document` gegen `:8080` liefern alle neun Seitenadressen die
Oberfläche — `/`, `/overview`, `/market`, `/consents`, `/settings`, `/my-data`,
`/delete-account`, `/jobs`, `/careers/test`. Ohne den Kopf antwortet die API:
`/market/status` → 422 `application/problem+json` (transfer-service),
`/transfers` → 401, `/jobs` → 200 `application/json`. Die Kollision ist damit
sauber getrennt.

Eine Falle beim Prüfen selbst: ein Bereitschaftstest auf „antwortet überhaupt"
ist zu wenig. Traefik läuft, bevor es seine `dynamic.yml` gelesen hat, und
antwortet in diesem Fenster auf **alles** mit `404` — auch auf `/`, das niemand
angefasst hat. Wer da misst, hält seine eigene Umbenennung für kaputt.

Eine Ausnahme bleibt nötig, falls je eine Adresse entsteht, zu der ein Browser
**navigieren muss** und die nicht zur Oberfläche gehört. Geprüft, als die Regel
entstand: kein Dienst antwortet mit einer Weiterleitung, es gibt keinen
OAuth-Rücksprung, und der Datenexport lädt über einen Blob.

**`/` und `/overview`.** `/` ist die Werbeseite; Angemeldete werden auf
`/overview` weitergeleitet. Vorher bediente `/` beide Seiten — das hielt die
Werbung aus dem Weg, gab der Übersicht aber keine eigene Adresse, und ein
Screenshot von `/` zeigte je nach Sitzung etwas anderes. `/overview` ohne
Sitzung geht zum Anmelden statt eine leere Seite mit einer Bitte darauf zu
zeigen.

### Der Inhalt von `/overview`: zwei Bereiche

Die Seite trägt eine dokumentierte Regel (`overview.tsx:27`): *gezählt wird nur,
was eine **Handlung** erwartet — eine Übersicht, die auch anzeigt, was von selbst
läuft, ist eine Liste, und Listen übersieht man.* Diese Regel bleibt, und die
Seite wird trotzdem voll: der obere Bereich bleibt die karge Handlungsliste, ein
klar abgesetzter Bereich **Stand** darunter informiert.

| Bereich | Person | Unternehmen |
|---|---|---|
| **Was liegt an** (Handlung) | offene Marktanfragen ✓, Anfragen nach dem Lebenslauf ✓, Gespräche, die auf dich warten ✓ | Transfers, die auf euch warten ✓ |
| **Stand** (Information, neu) | wie viele Unternehmen dich gerade sehen (Ledger), eigene Bewerbungen ohne Antwort | offene Stellen (`/companies/me/jobs`), Bewerbungen ohne erste Sichtung (`/companies/me/application-stats`), offene Einladungen |

✓ = existiert heute schon. Alles andere aus Quellen, die es gibt — **kein**
Vollständigkeits- oder Fortschrittsbalken über ein Profil (ADR-0022).

Gebaut wird der Stand-Bereich, wenn `overview.tsx` umgestellt wird (E3e), damit
E2.5 ein Routenschnitt bleibt und nicht Seiteninhalt mitzieht.

### Neue Routen, die aus abgespaltenen Formularen entstehen

Entschieden; gebaut jeweils in dem E3-PR, der die Herkunftsseite umstellt.

| neue Route | heute eingebettet in | E3-Gruppe |
|---|---|---|
| `/jobs/$id/apply` | `ApplyBox` in `jobs.tsx:320–420` | E3a |
| `/company/jobs/new`, `/company/jobs/$id/edit` | `company-jobs.tsx` (289 Zeilen) | E3d |
| `/company/team/invite` | `team.tsx` (221 Zeilen) | E3d |
| `/portfolio/new` (samt Upload) | `portfolio.tsx` (331 Zeilen) | E3b |
| `/resume/positions/$id` | Fieldsets in `resume.tsx` (290 Zeilen) | E3b |

Beim Lebenslauf mit Vorbehalt: mehrere Stationen gleichzeitig zu bearbeiten
spricht gegen eine Route je Station. Ob es eine Route je Station oder eine für
alle wird, entscheidet der Befund von E3b.

### `/company/admin` — angelegt, aber ehrlich beschriftet

Gewünscht: eine Route nur für den Administrator des jeweiligen Unternehmens, die
HR und Beschäftigte verwaltet und die Firmen-DNS für eine Subdomain verbindet
(`career.tsx` rechnet damit schon: *„Derselbe Code läuft später hinter
`karriere.firma.de`"*).

**Zwei Befunde, die dabeistehen müssen:**

1. **`admin` gegen `member` wird nirgends erzwungen** (CLAUDE.md), und gemessen
   prüft **keine** Route `tenant_id`. Die Kopfzeile *versteckt* nur; die
   Adressen sind eingetippt erreichbar, und dann antwortet der Server. Eine
   Admin-Seite, die HR verwaltet, ohne dass der Server die Rolle erzwingt, sieht
   wie Zugangskontrolle aus und ist keine. Das Erzwingen ist ein
   Backend-Schnitt und muss **vor** den Verwaltungshandlungen kommen.
2. Die DNS-Verbindung braucht Backend und Infrastruktur — eigener Schnitt.

Die Route entsteht deshalb in E3d zusammen mit `/company/team`, zunächst mit dem,
was es gibt (Mitgliederliste samt Rolle), und benennt das Fehlende, statt es
vorzugeben.

### Nicht gebaut: Agenten-Übersichten

Gewünscht waren Übersichten für einen Berateragenten und einen Scout-Agenten
(„gefundene Talente"). Beide entstehen hier **nicht**:

- Die KI-Naht hat genau **zwei** Verbraucher (`POST /profiles/me/draft`,
  `POST /jobs/draft`); beide formulieren Text auf Knopfdruck und speichern
  nichts (ADR-0024). Ein Berateragent existiert nicht.
- **Scout, Candidate Ranking, Salary Recommendation und Team Analyzer zielen auf
  Menschen** und sind deshalb ausdrücklich die Agenten, die ohne eigene Abwägung
  nicht gebaut werden (CLAUDE.md, ADR-0022). Eine Liste „gefundene Talente" ist
  eine von einer Maschine erzeugte Liste von Personen — genau der Fall.

Das ist keine Absage, sondern eine Zuordnung: es braucht seinen eigenen Schnitt
mit eigener Begründung, und der gehört nicht in eine Routenkarte.

## Soll — noch offen

- **Bleibt `/company/new` erreichbar?** Der Menüeintrag „Unternehmen anlegen"
  verschwindet (entschieden), aber die Route selbst ist offen: verschwindet sie,
  hat der spätere legitime Fall — jemand gründet zwei Jahre danach — keinen Weg
  mehr außer einem zweiten Konto. Bleibt sie erreichbar und nur unverlinkt, ist
  der Knopf weg und der Weg existiert. Gehört ins Befund-Gate von **E2.6**.

Die drei Punkte, die hier vorher offen standen — Sprache der Adressen, `/` mit
zwei Seiten, welche Formulare eigene Routen bekommen — sind oben entschieden.

## Regel für jede neue Route

**Mit eingetippter Adresse und F5 durchs Gateway auf `:8080` prüfen**, nicht mit
einem Klick. Ein Klick funktioniert immer, weil der Router im Browser umschaltet,
ohne zu fragen; nur der Deep-Link und der Reload gehen wirklich durchs Gateway,
und genau diese Hälfte lieferte einmal rohes JSON. Bei einer Route unter `/jobs/…`
kommt hinzu, dass jobs-service ein `GET /jobs/{id}` hat: dass die Dokumentregel
darüber gewinnt, ist zu **prüfen**, nicht anzunehmen.
