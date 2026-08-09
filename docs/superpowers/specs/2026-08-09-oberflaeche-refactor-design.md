# Die Oberfläche vollständig refaktorieren — und neu schneiden

Stand 09.08.2026. Löst Prompt E aus `docs/prompts-naechste-schritte.md` ein, mit
einer Erweiterung, die aus dem Gespräch kam und den Zuschnitt verändert.

## Warum dieses Dokument größer ist als der Prompt

Prompt E beschreibt einen Refactor: dieselbe Oberfläche, getragen von einem
Design-System statt von 885 Zeilen CSS in einer Datei. Der Beweis dafür ist ein
Screenshot-Vergleich, und der Satz dahinter lautet: *„Ein Refactor, der die
Oberfläche unbemerkt verändert, ist keiner."*

Diese Annahme hält nicht. Beim Durchsprechen stand fest, dass die Oberfläche
beim Umstellen auch **anders werden** soll — eigene Routen für Formulare, ein
Registrierungsweg, der Person und Unternehmen trennt, und weitere Änderungen,
die je Route entschieden werden. Das ist kein Zusatz, sondern eine andere
Aufgabe: aus „umstellen" wird „umstellen und neu schneiden".

Der Satz aus dem Prompt bleibt trotzdem gültig, nur wird er anders eingelöst.
Nicht „nichts hat sich geändert", sondern: **jede Änderung stand vorher
geschrieben, und der Screenshot zeigt sie.** Was sich unbemerkt ändert, ist
weiterhin ein Fehler.

## Der gemessene Ausgangspunkt

Nachgezählt am 09.08.2026, nicht aus dem Prompt übernommen. Wo der Prompt eine
andere Zahl nennt, steht sie daneben.

| Gemessen | Wert | Prompt |
|---|---|---|
| Routen (`apps/web/src/routes/*.tsx`, ohne Tests) | 26 | 26 ✓ |
| Zeilen in diesen Routen | 5.224 | 5.224 ✓ |
| `apps/web/src/styles.css` | 885 Zeilen | 885 ✓ |
| CSS gesamt (beide Dateien) | 1.119 Zeilen | 1.119 ✓ |
| Komponenten in `packages/ui` | 5 | 5 ✓ |
| `packages/ui/src/styles.css` | 234 Zeilen | 234 ✓ |
| **definierte `--wt-*`-Variablen** | **12** | 14 |
| **rohe `<select>`** | **4** (3 Routen + `CompanySwitcher` in `app.tsx`) | 3 |
| **Dateien mit eigenem `isPending`/`isLoading`** | **21** (76 Fundstellen) | 18 Routen |
| `role="alert"` von Hand | 39 Fundstellen in 23 Dateien | 23 Routen ✓ |
| `role="status"` von Hand | 19 | — |
| `aria-live` | **0** | 0 ✓ |
| `<dialog>` | **0** | 0 ✓ |
| `<table>` | **0** | 0 ✓ |
| `window.confirm` | **0** | — |
| Leerzustands-Sätze | 27 | „kein Leerzustand" ✓ |
| Testfälle `apps/web` | 385 | — |
| Testfälle `packages/ui` | 22 | — |

### Ein Fund, der nicht im Prompt steht: die Oberfläche hat zwei Paletten

`apps/web/src/styles.css` benutzt an **21 Stellen** `--wt-accent`,
`--wt-border` und `--wt-surface-muted`. **Keine dieser drei Variablen ist
irgendwo definiert.** Es greifen die Fallbacks, und die weichen von den echten
Tokens ab:

| Benutzt | Fallback | Echtes Token |
|---|---|---|
| `--wt-accent` | `#1f6f5c` | `--wt-green: #1b6a47` |
| `--wt-border` | `#d6d3cd` bzw. `#ece9e4` | `--wt-line: #dbe4db` |
| `--wt-surface-muted` | `#f4f1ec` | — |

Auch `packages/ui/src/components/switch.tsx` hängt daran (`.wt-switch__track`,
`.wt-switch__control[aria-checked="true"]`). Es gibt also heute zwei Grüntöne
und zwei Linienfarben, je nachdem welche Datei ein Element gestylt hat. Das ist
der erste Punkt, den E1 auflöst — und weil es eine **sichtbare** Änderung ist,
gilt der Screenshot-Beweis schon für E1 und nicht erst für E3.

Außerdem: `.wt-checkbox` existiert als CSS-Klasse in `packages/ui`, hat aber
**keine Komponente**, und drei Routen benutzen sie mit rohem `<input
type="checkbox">`. `.wt-visually-hidden` liegt umgekehrt in `apps/web`, obwohl es
ein Primitiv ist.

## Zuschnitt: neun PRs, sequenziell

Jeder Schnitt ist ein eigener PR. Erst wenn einer grün und gemergt ist, beginnt
der nächste — so steht es im Prompt, und der Grund steht in der Geschichte des
Repos (17 Commits auf einem Zweig waren einmal das größte Risiko im Projekt).

| # | Zweig | Inhalt | Zeilen Routen |
|---|---|---|---|
| 1 | `ui-designsystem` | E1: Tokens, Komponenten, Wächtertest, Screenshot-Werkzeug, ADR-0029. **Keine Route.** | 0 |
| 2 | `ui-drei-referenzrouten` | E2: login, register, home + Muster in `docs/frontend.md` | 268 |
| 3 | `oberflaeche-routenkarte` | E2.5: Soll-Routenkarte, Navigationsregeln, URL-Schema | — |
| 4 | `registrierung-person-oder-unternehmen` | E2.6: Person/Unternehmen bei der Registrierung | — |
| 5 | `ui-routen-bewerbung` | E3a: jobs, candidates, applications, career **+ eigene Bewerbungsroute** | 1.163 |
| 6 | `ui-routen-meine-daten` | E3b: portfolio, profile, resume, github | 1.104 |
| 7 | `ui-routen-konto` | E3c: consents, my-data, settings, account-deletion | 715 |
| 8 | `ui-routen-unternehmen` | E3d: company-jobs, company-transfers, team, company-profile, company-new, invitation | 1.153 |
| 9 | `ui-routen-markt-geruest` | E3e: market, transfers, overview, verify, auth-layout — dazu die Schale `app.tsx` (491, liegt außerhalb von `routes/`) | 821 |

**Die Summe stimmt, und das ist die Probe darauf, dass keine Route fehlt:**
268 + 1.163 + 1.104 + 715 + 1.153 + 821 = **5.224** — genau die gemessene
Gesamtzeilenzahl der 26 Routen.

**Die Zahl neun ist nicht endgültig.** Sprengt eine Verhaltensentscheidung eine
Gruppe, wird sie geteilt. E3 als ein PR wären 4.956 Zeilen gewesen — genau der
Fehler, vor dem der Prompt warnt.

## Das Befund-Gate

Vor dem Code jeder Gruppe (ab PR 2) entsteht ein Dokument **Befund & Soll** für
genau diese Routen:

1. **Ist-Stand je Route**, gemessen: Zeilen, Endpunkte, Zustände (lädt / leer /
   Fehler / fertig), Klassen, und ausdrücklich die **ADR-gebundenen Zusagen**,
   die dort hängen.
2. **Offene Verhaltensfragen**, je eine Zeile, mit den Folgen beider Antworten.
3. Nach der Entscheidung: **Soll-Stand**, und was das an Tests kostet.

Erst danach Code. Der Grund ist nicht Formalität: die Routentests behaupten
heutiges Verhalten, und wer Verhalten ändert, ändert Tests — ein Testlauf, der
nach einer Verhaltensänderung grün bleibt, hat das Verhalten nicht geprüft.

## E1 — Das Design-System, vollständig

Berührt **keine** Route. Die 385 Testfälle in `apps/web` bleiben unverändert
grün; ist das nicht so, hat E1 seinen Rahmen verlassen.

### E1.1 — Eine Palette, ein Token-Satz

Zuerst die Palette vereinigen, dann Komponenten bauen — andernfalls erbt jede
neue Komponente die Zweideutigkeit.

`--wt-accent` wird **Alias auf `--wt-green`**, `--wt-border` **Alias auf
`--wt-line`**. Das verschiebt 21 Stellen um einen kleinen, aber sichtbaren
Farbwert. Das ist beabsichtigt und gehört genannt; die Alternative (die
Fallback-Werte als Definition festschreiben) würde beide Paletten dauerhaft
einfrieren.

Neu dazu, jeweils weil der Wert heute mehrfach hart im CSS steht:

| Gruppe | Tokens | Begründung |
|---|---|---|
| Farbrollen | `--wt-accent`, `--wt-border`, `--wt-surface-muted`, `--wt-danger` (`#b4392f`, 4× hart), `--wt-danger-soft` | benutzt, nie definiert |
| Abstände | `--wt-space-1` … `--wt-space-7` | wiederkehrende `gap`/`padding`-Werte |
| Schrift | `--wt-text-xs` … `--wt-text-2xl`, `--wt-leading-tight`, `--wt-leading-normal` | 0.8 / 0.85 / 0.92rem und 1.45 / 1.55 stehen vielfach da |
| Radien | `--wt-radius-pill: 999px` | 3× hart |
| Zustände | `--wt-focus-ring`, `--wt-disabled-opacity: 0.55` | das Fokus-Rezept steht 3× **verschieden** da |
| Bewegung | `--wt-transition: 160ms ease` + ein `prefers-reduced-motion`-Block | 4× hart; einen solchen Block gibt es heute nicht, obwohl `transform` animiert wird |

**Kein Dark-Mode.** Der Prompt schließt ihn aus, solange niemand ihn anfordert.

**Wächtertest.** Ein Test scheitert, wenn ein `var(--wt-…)` benutzt wird, das in
`packages/ui/src/styles.css` nicht definiert ist — getrennt für beide CSS-Dateien,
damit auch `apps/web` gegen die Definitionen des Pakets geprüft wird. Genau
dieser Fehler war da; ohne Wächter kommt er zurück. Das ist das Hausidiom
(`test_env_examples_are_real.py`, `test_k8s_matches_compose.py`).

### E1.2 — Das Komponenteninventar

Fünf vorhandene (`Button`, `Card`, `Field`, `TextArea`, `Switch`) plus
fünfzehn neue.

**A — Zustände.** Die größte gemessene Lücke.

| Komponente | Verhalten | Verbraucher heute |
|---|---|---|
| `Loading` | `<p role="status">{label}</p>`. Das Label kommt **vom Aufrufer** | 21 Dateien. Die Texte sind routenspezifisch („Portfolio wird geladen…", „Freigaben werden geladen…", „Marktstatus wird geladen…") — ein hartkodiertes „Wird geladen…" kostete über 20 Tests |
| `Alert` | `variant="error"` → `role="alert"`; `variant="notice"` → `role="status"`. Der Unterschied ist Verhalten: `alert` unterbricht, `status` nicht | 39 Fundstellen, über 20 davon mit der Klasse `auth__alert` auf Seiten, die nichts mit Auth zu tun haben |
| `Empty` | Titel + optionale Handlung | 27 Leerzustands-Sätze |
| `LiveRegion` + `useAnnounce` | `aria-live="polite"`, Text wird bei Änderung angesagt | **0× `aria-live` heute**: nach einer Mutation wird nichts angesagt |
| `Skeleton` | `aria-hidden="true"`, **immer** gepaart mit einem `Loading`-Label in einer Live-Region | **kein Verbraucher heute.** Entscheidung in E3 |

`Skeleton` ist ausdrücklich ohne Aufrufer gebaut, weil im Gespräch stand, dass
der Bedarf während des Refactorings entsteht. Findet keine Route in E3 einen
Grund, fliegt er wieder raus — dieses Repo hat `worker-ai`, `worker-files` und
`worker-messaging` genau wegen „kein Verbraucher" gelöscht.

Die Zugänglichkeitsregel für `Skeleton` ist nicht frei gewählt: ein Skeleton ist
Dekoration und gehört vor Screenreadern versteckt, und der Ladezustand wird
stattdessen in einer höflichen Live-Region angesagt. Ein sichtbares Skeleton
ohne Ansage ist für Screenreader ein stummer Bildschirm.

**B — Formular.**

| Komponente | Entscheidung |
|---|---|
| `Select` | **Gestyltes natives `<select>`** in derselben Hülle wie `Field` (Label, Hinweis, Fehler, `aria-describedby`). Begründung: das APG-Muster für eine Combobox verlangt `role="combobox"`, `aria-controls`, `aria-expanded`, `aria-autocomplete` **und** `aria-activedescendant`; keiner der vier gemessenen Selects braucht Filtern oder Autocomplete — es sind Wertwähler. Der Prompt sagt es selbst: wer es nicht sauber hinbekommt, nimmt das native Element. Hier ist das native nicht der Kompromiss, sondern das Richtige |
| `Checkbox` | `.wt-checkbox` existiert als CSS ohne Komponente, 5 rohe Verwendungen. Der Unterschied zu `Switch` wird **in der Komponente** dokumentiert: Checkbox = gilt nach dem Absenden, Switch = gilt sofort. Bei einer Einwilligung ist das keine Kosmetik |
| `Fieldset` / `RadioGroup` | `<fieldset>` + `<legend>`. `/markt` hat die einzige Radiogruppe (drei Zustände), 3× `<fieldset>` bei den Lebenslauf-Stationen |

**C — Gerüst.**

| Komponente | Verbraucher |
|---|---|
| `Page` / `PageHeader` | `.page`, `.page--narrow`, `.page__header`, `.page__lead`, `.page__note` — 136 gemessene `page*`-Klassennutzungen, der größte CSS-Block. Bekommt einen optionalen `back`-Platz („Zurück zu …"), weil eigene Routen für Formulare diesen Bedarf vervielfachen |
| `Row` | Titel / Meta / Aktionen. `.requests__row` (33×), `.team li`, `.overview li` |
| `DescriptionList` | 2× `<dl>` (Transfer-Angebot) |
| `Badge` | Anfragezähler. **ADR-0022-Grenze in der Komponente notiert**: zählt Vorgänge, niemals Personen |
| `VisuallyHidden` | liegt heute in `apps/web/src/styles.css`, ist aber ein Primitiv |

**D — Erwartet, aber heute ohne Aufrufer.**

`Dialog` — auf dem **nativen `<dialog>`** mit `showModal()`. Der Browser liefert
davon vier der fünf Forderungen des Prompts: Fokuseinschluss, Esc schließt,
inerter Hintergrund, `aria-modal="true"` (dazu Top-Layer und `::backdrop`).
Selbst zu schreiben bleiben: Fokus zurück zum auslösenden Element,
Anfangsfokus über `autofocus`, `aria-labelledby` auf die Überschrift, und ein
echter Schließen-Knopf. `role="dialog"` und `aria-modal` dürfen **nicht** von
Hand gesetzt werden — `showModal()` setzt sie schon.

> **Beweisgrenze, die genannt werden muss:** jsdom implementiert
> `HTMLDialogElement.showModal()` nicht (offener jsdom-Issue #3294). Die
> Fokusfalle ist in Vitest also **nicht** beweisbar. In der Testumgebung wird
> `show()`/`showModal()`/`close()` geshimmt, und der jsdom-Test behauptet nur
> unsere vier eigenen Pflichten. Fokusfalle und Esc werden in **einer
> Playwright-Prüfung in echtem Chromium** belegt — oder gar nicht behauptet.

`Toast` — mit `LiveRegion`. Vorbehalt, der in der Komponente steht: eine
Meldung, die von selbst verschwindet, ist schlechter als eine, die stehen
bleibt, sobald jemand danach handeln muss. Die Zusage aus ADR-0027 §6 („läuft")
darf **kein** Toast werden.

**Nicht gebaut — Abweichung vom Prompt, mit Grund.**

- **`Table`.** Der Prompt zählt „KEIN `<table>`" als Lücke. Die gemessenen
  Listen (Mannschaft, Anfragen, Kandidaten) sind aber Zeilen, und sie in
  Tabellen zu verwandeln ändert das DOM, auf das die Routentests zeigen — das
  wäre Redesign, nicht Systembau. Trifft E3 auf einen echten tabellarischen
  Fall, kommt `Table` dort mit eigener Begründung.
- `Tabs` / `Accordion`: natives `<details>` trägt das Kopfzeilenmenü heute und
  kann Tastatur und Escape von sich aus.
- `Pagination`: „Mehr laden" ist ein `Button` und bleibt einer.
- `Progress`: ADR-0022 erlaubt einen Balken nur für einen Upload; der
  Portfolio-Upload sagt heute „Wird hochgeladen…" als Text und braucht keinen.
- `Tooltip`, `Avatar`: kein Verbraucher, und ein Avatar wäre eine Person als
  Bild dort, wo es um Fähigkeiten geht.

### E1.3 — Beweis für E1

- Jede neue Komponente hat einen Test, der **Verhalten** prüft — Tastatur,
  Fokus, aria — nicht Klassennamen. Ausgangswert: 22 Testfälle in
  `packages/ui`; Ziel etwa 70.
- Die 385 Testfälle in `apps/web` laufen **unverändert** grün.
- `pnpm check && pnpm test && pnpm build`, alle drei — die CI fährt sie auch.
- Screenshot-Vergleich der Palettenvereinigung (siehe unten), weil sie sichtbar
  ist.
- ADR-0029 hält die Entscheidungen fest: eigene Primitives statt Bibliothek,
  natives Element vor selbstgebautem, kein Bauteil, das einen Menschen als Zahl
  darstellt.

## E2 — Drei Routen als Referenz

`login.tsx` (82), `register.tsx` (121), `home.tsx` (65). Ziel ist ein **Muster
zum Abschreiben**, nicht drei hübsche Seiten. Verhalten bleibt hier
unverändert — die Verhaltensänderung an der Registrierung ist bewusst ein
eigener Schnitt (E2.6), damit das Muster ohne zweite Variable bewiesen wird.

Das Muster, das nach `docs/frontend.md` gehört:

```
<Page title lead? note? back?>
  isPending → <Loading label="X wird geladen…" />     Label routenspezifisch
  isError   → <Alert variant="error">{message}</Alert>  Servertext, nie erfunden
  leer      → <Empty title="…" action? />
  fertig    → Inhalt

Mutation:  <Button disabled={m.isPending}>{m.isPending ? "…läuft" : "…"}</Button>
Erfolg:    useAnnounce() + <Alert variant="notice">
```

Verbindlich: **Fehler vor Leer vor Inhalt.** CSS gehört nach `packages/ui`,
außer es ist wirklich das Layout genau einer Seite.

`docs/frontend.md` ist grob veraltet — Stand 2026-07-31, „Two routes", „`Button`
and `Card`", Node 24. E2 schreibt den Abschnitt **neu**, nicht dazu.

Was nicht verloren gehen darf:

- Die Anmeldeseite zeigt „Danach geht es zurück zu: `<Stelle>`", wenn eine
  Absicht gemerkt ist (`apps/web/src/jobs/ZurueckHinweis.tsx`).
- Nach erfolgreichem Anmelden geht es zu `/jobs?stelle=<id>`, falls gemerkt.
  **Dieses Ziel ändert sich erst in E3a**, wenn die Bewerbungsroute existiert —
  nicht hier.
- Die Registrierung zeigt für eine bekannte Adresse **dieselbe** Antwort wie
  für eine neue. Ein Unterschied verriete Plattformmitgliedschaft, ohne den
  Consent-Ledger zu fragen.

## E2.5 — Die Soll-Routenkarte

Eigener Schnitt, weil „eigene Routen" nichts Lokales ist: es ändert Router,
Kopfzeilennavigation, Deep-Links, Gateway-Regeln und E2E-Reisen. Verteilt über
fünf E3-PRs würde der Router fünfmal umgeschrieben und die Navigation driftet.

Inhalt:

1. **Routentabelle Ist → Soll** für alle 26 Routen: bleibt / wird geteilt /
   wird abgespalten / verschwindet, mit Ziel-URL.
2. **Navigationsregeln**: was ein Mensch ohne Unternehmen sieht, was ein
   Mitglied sieht, was ein Administrator sieht. Heute hängt das an
   `user.tenant_id` und den Mitgliedschaften; die Regeln stehen nirgends
   geschrieben.
3. **URL-Schema** für abgespaltene Formulare, samt der Prüfung, die jede neue
   Route braucht (siehe „Gateway" unten).
4. `docs/frontend.md` bekommt die Karte; der Router und die Kopfzeile werden
   danach ausgerichtet.

## E2.6 — Registrierung: Person oder Unternehmen

**Gemessener Ist-Stand.** Der Eintrag „Unternehmen anlegen" steht in
`apps/web/src/app.tsx:152` im Menü „Mein Konto" und ist für **jeden**
Angemeldeten sichtbar. `company-new.tsx:25` weist private Adressen ab — die
Absage kommt also erst **nach** dem Klick. `createCompany(name)` schickt nur
den Namen; die Domain leitet der Server aus der bestätigten Adresse ab
(ADR-0019), der Erzeuger wird `admin`.

**Soll.** Bei der Registrierung wird gewählt: als Person oder als Unternehmen.
Wer als Person registriert ist, sieht in der Anwendung **keinen** Knopf, ein
Unternehmen anzulegen.

**Die Folgen, die daran hängen — keine davon ist Kosmetik:**

1. **Die Wahl muss die Mail-Runde überleben.** ADR-0019 leitet die Domain aus
   der **bestätigten** Adresse ab, und bei der Registrierung ist das Konto
   `PENDING`. Das Unternehmen kann also erst nach `POST /auth/verify-email`
   entstehen. Empfehlung: die Absicht clientseitig merken, genau wie
   `apps/web/src/jobs/intent.ts` es für eine Stelle über das Anmelden hinweg
   schon tut, und den Unternehmensschritt direkt nach der Bestätigung anbieten.
   Kein Backend-Eingriff, ein erprobtes Muster im Haus. Die Alternativen —
   Absicht am `PENDING`-Nutzer speichern (Migration) oder das Unternehmen
   serverseitig bei der Bestätigung anlegen (legt aus einem vor der Bestätigung
   getippten Namen ein Unternehmen an) — sind beide größer.
2. **Die Freemail-Prüfung wandert nach vorn.** Wer „Unternehmen" wählt und eine
   private Adresse einträgt, muss es **an dieser Stelle** erfahren, nicht nach
   der Bestätigung. Die Ablehnung selbst spricht weiterhin der Server aus (422);
   die Oberfläche darf nur die Sichtbarkeit steuern.
3. **Offene Entscheidung: was wird aus `/company/new`?** Verschwindet die Route,
   hat der spätere legitime Fall — jemand gründet zwei Jahre danach ein
   Unternehmen — keinen Weg mehr außer einem zweiten Konto. Bleibt sie
   erreichbar, aber unverlinkt, ist der Knopf weg und der Weg existiert.
   **Diese Frage gehört ins Befund-Gate von E2.6 und wird nicht vorab
   entschieden.**
4. **„Keine Unternehmensknöpfe" ist nicht „keine Unternehmensfunktionen".**
   `/invitation` existiert: wer eingeladen wird, wird Mitglied und bekommt das
   Unternehmensmenü über `tenant_id`, ohne je eines registriert zu haben — eine
   Person darf für mehrere Unternehmen handeln (ADR-0018). Der Plan verspricht
   das nicht weg.

## E3 — Die übrigen 23 Routen, in Gruppen

Beginnt erst, wenn E2 gemergt ist und das Muster in `docs/frontend.md` steht.
Reihenfolge nach Größe, die größten zuerst, weil dort der meiste Doppelcode
steckt. Je Gruppe erst Befund, dann Code.

### E3a — Bewerbung (1.163 Zeilen)

`jobs.tsx` (457), `candidates.tsx` (418), `applications.tsx` (146),
`career.tsx` (142).

**Die Bewerbung bekommt eine eigene Route.** Gemessener Ist-Stand: `ApplyBox`
steckt in `jobs.tsx:320–420`, klappt innerhalb der Stellenkarte auf und wird
über `?stelle=<uuid>` vorgeklappt, wenn jemand vom Anmelden zurückkommt.

Folgen:

- Das Ziel der gemerkten Absicht wandert von `/jobs?stelle=<id>` auf die neue
  Route. Das ist eine **Verbesserung** — man landet auf der Handlung statt auf
  einer Liste, die eine Box aufklappt — und deshalb gehört sie hierher und
  nicht nach E2.
- Etwa 25 Zeilen Sonderlogik in `jobs.tsx` (die einzeln geholte Stelle, die
  vorn einsortiert wird, damit sie nicht durch Filter und Seitengrenzen fällt)
  werden überflüssig. Die Ersparnis gehört gemessen in den PR.
- Die Route braucht die Stellendaten auf einem kalten Deep-Link, also
  `getJob(id)`, plus einen Zustand für „Stelle unbekannt oder zurückgezogen".
- Die E2E-Reisen `jobs-journey.spec.ts` und `application-journey.spec.ts` gehen
  durch das eingebettete Formular und ändern sich mit.

**Zusagen, die kein Refactor anfassen darf:** die Passung ist eine Liste mit
Haken („Du hast 2 von 3 genannten Fähigkeiten"), niemals eine Zahl oder ein
Balken (ADR-0022); wer keine Fähigkeiten eingetragen hat, bekommt **kein „0 von
3"** zu sehen, sondern den Hinweis aufs Profil — nichts gesagt ist nicht nichts
gekonnt.

### E3b — Meine Daten (1.104 Zeilen)

`portfolio.tsx` (331), `profile.tsx` (297), `resume.tsx` (290),
`github.tsx` (186).

**Zusagen:** Bei Profilen heißt `404` „verborgen **oder** nicht vorhanden", und
beides muss gleich aussehen; `403` heißt „kein aktives Unternehmen"; `503` heißt
„das Ledger schweigt" und darf **nicht** wie „nicht gefunden" aussehen. Beim
Lebenslauf gilt: die Anfrage ist nicht die Erlaubnis — nach einem Widerruf
bleibt die Anfrage `GRANTED` und die Anzeige leer.

### E3c — Konto und Freigaben (715 Zeilen)

`account-deletion.tsx` (230), `consents.tsx` (183), `my-data.tsx` (171),
`settings.tsx` (131).

**Zusagen aus ADR-0027 §6, wörtlich zu erhalten:** der Text steht **vor** dem
Knopf; die Bestätigung hat zwei Schritte mit **anders formuliertem** zweitem
Knopf; danach heißt es „läuft", nicht „erledigt"; es gibt **keinen**
Fortschrittsbalken; und der Erfolgszustand hat **Vorrang vor der
Anmeldeaufforderung**, weil die Seite die Sitzung löscht und die Schale sonst
direkt nach dem Löschen „Bitte anmelden" zeigt. Kein Dialog an dieser Stelle:
zwei bewusste Schritte inline sind die Zusage, und ein Dialog wäre eine andere.

### E3d — Unternehmen (1.153 Zeilen)

`company-jobs.tsx` (289), `company-transfers.tsx` (232), `team.tsx` (221),
`company-profile.tsx` (183), `invitation.tsx` (127), `company-new.tsx` (101).

Hängt an der Entscheidung aus E2.6 über `/company/new`.

### E3e — Markt und Gerüst (821 Zeilen Routen + 491 Schale)

`market.tsx` (290), `transfers.tsx` (189), `overview.tsx` (189),
`verify.tsx` (113), `auth-layout.tsx` (40) und die Schale `app.tsx` (491,
darunter der vierte rohe `<select>` im `CompanySwitcher`).

Zuletzt, weil die Schale erst dann umgestellt wird, wenn alle Seiten darin
stehen.

### Ziel am Ende

`apps/web/src/styles.css` ist weitgehend leer, weil das Aussehen in
`packages/ui` liegt. **Heute: 885 Zeilen.** Jeder PR nennt die neue Zahl.

## Der Screenshot-Beweis

Eigener Ordner `apps/web/e2e-shots/` mit eigener Playwright-Konfiguration,
eigenem Skript, Ausgabe nach `.screenshots/<tag>/` und einem
`.gitignore`-Eintrag.

**Nicht** in `apps/web/e2e/`: `scripts/validate.sh` zählt „Reisen" aus dem
Playwright-Protokoll, und ein Screenshot-Spec dort blähte die Zahl auf — damit
wäre genau der Zähler entwertet, der eingebaut wurde, weil sich einmal alle 16
Reisen übersprungen haben und der Bericht „Alles grün" sagte.

Keine eingecheckten Referenzbilder und kein CI-Gate: Schriftglättung und
Plattform (darwin lokal, linux in der CI) erzeugten Fehlalarme, und die CI
fährt den Stapel nicht.

Ablauf je PR:

```bash
docker compose up -d
pnpm --filter @workertransfer/web run shots -- --tag vorher
# … umstellen …
pnpm --filter @workertransfer/web run shots -- --tag nachher
```

Anmeldung über die vorhandenen Helfer aus `apps/web/e2e/stack.ts`.

**Was der Vergleich ab jetzt beweist:** nicht „nichts hat sich geändert",
sondern „das hier hat sich geändert, und zwar absichtlich". Jeder PR trägt
deshalb zwei Listen: die beabsichtigten Änderungen samt der Entscheidung, die
sie deckt, und die Testzahl, die sich deswegen bewegt hat. Was sich unbemerkt
ändert, bleibt ein Fehler.

## Gateway: jede neue Route braucht eine Deep-Link-Prüfung

`/jobs` ist Seite **und** API-Präfix; dasselbe gilt für `/applications`,
`/transfers`, `/github`. Gelöst über `Sec-Fetch-Dest: document` in
`docker/traefik/dynamic.yml` mit `priority: 200`.

Für jede in E2.5/E3 geborene Route gilt deshalb: **eingetippte Adresse und F5
durchs Gateway auf `:8080`**, nicht ein Klick in der Anwendung. Ein Klick
funktioniert immer, weil der Router im Browser umschaltet, ohne zu fragen — nur
der Deep-Link und der Reload gehen wirklich durchs Gateway, und genau diese
Hälfte lieferte einmal rohes JSON. Bei `/jobs/<uuid>/bewerben` kommt hinzu, dass
jobs-service ein `GET /jobs/{id}` hat: dass die Dokumentregel darüber gewinnt,
ist zu **prüfen**, nicht anzunehmen.

## Was nicht Teil dieser Arbeit ist

- **i18n.** Die Oberfläche ist deutsch und hartkodiert, die Tests prüfen die
  Literale direkt. Texte ändern sich nur, wo es ausdrücklich gewollt ist; jede
  Änderung kostet einen Test.
- **Tailwind, Radix, eine Komponentenbibliothek.** Entschieden und bleibt so
  (CLAUDE.md).
- **Dark Mode.**
- **Jedes Bauteil, das einen Menschen als Zahl darstellt** (ADR-0022): kein
  Score, kein Prozent, kein Ranking, kein Fortschrittsbalken über Personen. Ein
  Balken für einen Upload ist in Ordnung; einer über die „Vollständigkeit" eines
  Profils ist genau das, was ADR-0022 ausschließt.

## Risiken

| Risiko | Gegenmaßnahme |
|---|---|
| Aus dem Refactor wird eine Neuentwicklung, und niemand merkt, wann die Grenze fiel | Befund-Gate je Gruppe; beabsichtigte Änderungen stehen **vor** dem Code geschrieben |
| Die Palettenvereinigung verändert 21 Stellen sichtbar | Screenshot-Vergleich schon für E1, Änderung im PR benannt |
| `Dialog`, `Toast`, `Skeleton` bleiben ohne Aufrufer stehen | E3 nimmt sie in Dienst oder löscht sie; die Entscheidung steht im letzten E3-PR |
| Eine neue Route liefert am Deep-Link rohes JSON | Prüfung mit eingetippter Adresse und F5 auf `:8080`, je Route |
| Die Testzahl sinkt unbemerkt, weil Tests mit dem Verhalten verschwinden | `make validate` nennt die Zahl; jeder PR nennt sie auch |
| Neun PRs sequenziell dauern lang, Zwischenzustand ist gemischt | Genau deshalb sequenziell: `packages/ui` und `styles.css` koexistieren, bis die letzte Gruppe umgestellt ist |

## Definition of Done

**E1:** 20 Komponenten mit Verhaltenstests; ein Token-Satz, ein Wächtertest
gegen undefinierte Tokens; `pnpm check/test/build` grün; die 385 Testfälle in
`apps/web` unverändert grün; ADR-0029 geschrieben; Screenshot-Vergleich der
Palette.

**E2:** Drei Routen auf dem System, Verhalten unverändert, das Muster steht in
`docs/frontend.md`, E2E grün.

**E2.5:** Routentabelle Ist → Soll für alle 26 Routen, Navigationsregeln und
URL-Schema in `docs/frontend.md`.

**E2.6:** Die Wahl steht in der Registrierung, überlebt die Bestätigungsmail,
und eine als Person registrierte Person sieht keinen Unternehmensknopf. Die
Entscheidung über `/company/new` ist getroffen und aufgeschrieben.

**E3 je Gruppe:** `pnpm check/test/build` grün, E2E grün, Screenshot-Vergleich
im PR, `styles.css`-Zeilenzahl genannt, beabsichtigte Änderungen aufgelistet.

**Am Ende:** `apps/web/src/styles.css` ist weitgehend leer, `packages/ui` trägt
die Oberfläche, und `docs/frontend.md` beschreibt, wie eine Route aussieht.
