# ADR-0029: Das Design-System trägt die Oberfläche — natives Element vor selbstgebautem

- **Status:** angenommen (10.08.2026)
- **Betrifft:** `packages/ui`, `apps/web`
- **Zusammenhang:** ADR-0022 (kein Mensch als Zahl), ADR-0027 §6 (die Zusagen der
  Löschseite). Umsetzung von Schnitt E1 aus
  `docs/superpowers/specs/2026-08-09-oberflaeche-refactor-design.md`.

## Kontext

`packages/ui` bot fünf Bauteile (`Button`, `Card`, `Field`, `TextArea`,
`Switch`) auf 234 Zeilen CSS. `apps/web` trug dagegen 26 Routen mit 5.224 Zeilen
und 885 Zeilen CSS in **einer** Datei. Fast jede Route baute ihre Darstellung
selbst, und was fehlte, wurde überall neu erfunden — nachgezählt am 09.08.2026:

- **21 Dateien** behandelten `isPending` selbst. Es gab keine Ladeanzeige.
- **39 Fundstellen** setzten `role="alert"` von Hand, über zwanzig davon mit der
  Klasse `auth__alert` auf Seiten, die nichts mit Auth zu tun haben.
- **0×** `aria-live`: nach einer Mutation wurde nichts angesagt.
- **0×** `<dialog>`, **0×** `<table>`, **4** rohe `<select>`.
- **27** Leerzustands-Sätze, jeder anders gebaut.

Dazu ein Fund, der in keiner Aufgabenliste stand: **die Oberfläche hatte zwei
Paletten.** `--wt-accent`, `--wt-border` und `--wt-surface-muted` wurden an 22
Stellen benutzt und **nirgends definiert**. Es griffen die Fallbacks, und die
wichen von den echten Tokens ab (`#1f6f5c` gegen `--wt-green: #1b6a47`,
`#d6d3cd` gegen `--wt-line: #dbe4db`). Welchen Grünton ein Element trug,
entschied also die Datei, in der es gestylt wurde.

## Entscheidung

### 1. Eigene Primitives, keine Bibliothek

Kein Tailwind, kein Radix, keine Komponentenbibliothek — bestätigt, nicht neu
entschieden (CLAUDE.md). Die Folge ist eine Pflicht: **weil keine fremden
Primitives benutzt werden, ist Zugänglichkeit unsere Arbeit** und nicht die
einer Bibliothek.

### 2. Natives Element vor selbstgebautem

Wo ein HTML-Element die Zugänglichkeit schon leistet, wird es benutzt statt
nachgebaut. Drei Belege, jeder mit einem konkreten Grund:

- **`Select` ist ein natives `<select>`.** Das APG-Muster für eine Combobox
  verlangt `role="combobox"`, `aria-controls`, `aria-expanded`,
  `aria-autocomplete` **und** `aria-activedescendant`. Keiner der vier
  Bestandswähler braucht Filtern oder Autocomplete — es sind Wertwähler. Ein
  hübsches Listenfeld, das man mit der Tastatur nicht bedienen kann, ist
  schlechter als ein hässliches, das man bedienen kann.
- **`Dialog` ist ein natives `<dialog>` mit `showModal()`.** Der Browser liefert
  Fokuseinschluss, Esc schließt, inerten Hintergrund, `aria-modal="true"`,
  Top-Layer und `::backdrop`. Selbst geschrieben sind nur die drei Dinge, die die
  Plattform nicht übernimmt: Fokus zurück zum Auslöser, `aria-labelledby`, ein
  echter Schließen-Knopf. `role` und `aria-modal` werden **nicht** von Hand
  gesetzt — `showModal()` setzt sie, und beides doppelt zu setzen ist der
  häufigste Fehler an diesem Element.
- **`RadioGroup` sind native `<input type="radio">` mit gemeinsamem `name`.** Der
  gemeinsame Name ist es, der den Browser die Gruppe bilden lässt; erst dadurch
  bewegen die Pfeiltasten den Fokus innerhalb der Gruppe, und die Gruppe verhält
  sich beim Tabben wie ein Element.

### 3. Eine Palette, ein Definitionsort, keine Fallbacks

Alle `--wt-*`-Definitionen stehen in `packages/ui/src/styles/tokens.css` und
nirgends sonst. `--wt-accent` und `--wt-border` sind **Aliase** auf
`--wt-green` und `--wt-line`, keine zweiten Werte.

**Ein `var(--wt-x, fallback)` ist verboten**, auch wenn das Token definiert ist.
Der Fallback war genau der Tarnweg, auf dem die zweite Palette entstanden ist:
er sieht aus wie ein Token und ist ein eigener Farbwert, der greift, sobald
jemand umbenennt — still, denn es sieht ja weiterhin gut aus.

Zwei Wächtertests halten das fest (`packages/ui/src/styles.test.ts`,
`apps/web/src/styles.test.ts`). Beide prüfen **zuerst, dass sie überhaupt etwas
lesen**: ein Wächter, der bei einer Umbenennung still auf null Vergleiche
schrumpft, meldet Ordnung über Dateien, die er nicht mehr findet — dieselbe
Fehlerklasse wie ein E2E-Lauf, der sich vollständig überspringt und trotzdem
grün berichtet.

Das CSS liegt als eine Datei je Bauteil in `packages/ui/src/styles/`, gehalten
von einer Einstiegsdatei aus reinen `@import`-Zeilen. Zwanzig Bauteile in eine
Datei zu legen hätte in `packages/ui` genau den Fehler wiederholt, dessen
Behebung dieser Schnitt ist. Dass Vite die Kette auflöst, ist am gebauten
Artefakt geprüft, nicht angenommen.

**Kein Dark-Mode**, solange niemand ihn anfordert. **Kein i18n**: die Oberfläche
ist deutsch und hartkodiert, und die Tests prüfen die Literale direkt.

### 4. Kein Bauteil, das einen Menschen als Zahl darstellt

ADR-0022 gilt im Design-System als Bauvorschrift: kein Score, kein Prozent, kein
Ranking, kein Fortschrittsbalken über Personen.

Die Grenze verläuft am `Badge`, und sie steht in seinem Docstring: er zählt
**Vorgänge**. „3 offene Anfragen" ist erlaubt; „Profil 60 % vollständig" oder
„Rang 4 von 12" ist es nicht. Aus demselben Grund gibt es **kein `Progress`** —
ein Balken für einen Upload wäre in Ordnung, aber der Portfolio-Upload sagt
heute Text, und ein Bauteil ohne Verbraucher wäre die Einladung, es an eine
Person zu hängen.

Ein `Badge` zeigt außerdem bei `count <= 0` **nichts**: eine Null ist keine
Nachricht, sondern ein Aufmerksamkeitsanspruch ohne Anlass.

### 5. Beweisgrenzen werden benannt, nicht überspielt

**jsdom implementiert `HTMLDialogElement.showModal()` nicht** — gemessen mit
jsdom 30: `TypeError: dialog.showModal is not a function` (jsdom-Issue #3294).

Der Ersatz in `packages/ui/src/test/setup.ts` setzt deshalb genau das
`open`-Attribut und löst beim Schließen das `close`-Ereignis aus — und ahmt die
Fokusfalle, Esc und den inerten Hintergrund **absichtlich nicht** nach. Täte er
das, wären unsere Tests Behauptungen über unseren eigenen Ersatz und kein Beweis
über den Browser.

Daraus folgt eine offen genannte Lücke: **Fokusfalle und Esc sind in E1 nicht
bewiesen.** Der Beweis in echtem Chromium kommt mit dem ersten Verbraucher in
E3, weil E1 keine Route anlegt, die man öffnen könnte. Er wird bis dahin nicht
behauptet.

### 6. Drei Bauteile ohne Verbraucher, mit Ablaufdatum

`Skeleton`, `Dialog` und `Toast` haben heute **keinen Aufrufer**. Sie sind
gebaut, weil beim Zuschnitt abgesprochen wurde, dass der Bedarf während des
Refactorings entsteht.

**Der letzte E3-PR entscheidet über jeden der drei: Verbraucher oder Löschung.**
Dieses Repo hat `worker-ai`, `worker-files` und `worker-messaging` genau wegen
„kein Verbraucher" gelöscht, und ein Bauteil, das niemand aufruft, ist keine
Vorbereitung, sondern eine Behauptung über die Zukunft.

Nicht gebaut, obwohl in der Lückenliste genannt: **`Table`**. Die
Bestandslisten (Mannschaft, Anfragen, Kandidaten) sind Zeilen; sie in Tabellen
zu verwandeln ändert das DOM, auf das die Routentests zeigen — das wäre
Redesign, nicht Systembau.

## Folgen

- 20 Bauteile, jedes mit einem Test, der **Verhalten** prüft — Rolle,
  zugänglicher Name, Beschreibung, Tastatur, Fokus — und nicht Klassennamen.
  `packages/ui` steht bei 81 Testfällen (Ausgangswert 22).
- Die 385 Testfälle in `apps/web` sind unverändert grün: E1 hat keine Route
  angefasst.
- Drei **sichtbare** Änderungen, alle beabsichtigt und im PR bebildert:
  `--wt-accent` `#1f6f5c` → `#1b6a47`, `--wt-border` `#d6d3cd` → `#dbe4db`,
  `--wt-surface-muted` `#f4f1ec` → aus `--wt-surface` abgeleitet.
- `apps/web/src/styles.css` ist noch **885 Zeilen** groß. E1 verschiebt nichts
  aus den Routen; das ist E2 und E3.
- Eine bekannte Lücke im Screenshot-Beweis: die drei Bestandsnutzer von
  `.wt-checkbox` liegen hinter der Anmeldung und sind nicht bebildert. Der
  Wechsel von `display:flex` auf `display:grid` ist nur am gebauten Artefakt
  belegt.

## Alternativen

- **Eine Komponentenbibliothek nehmen** (Radix, Ark, React Aria). Hätte
  Zugänglichkeit mitgebracht und die Entscheidung aus CLAUDE.md umgedreht — samt
  einer Abhängigkeit, deren Bauteile Meinungen über Darstellung mitbringen. Nicht
  gemacht.
- **Fallback-Werte als Definition festschreiben** statt Aliase zu bilden. Wäre
  ohne sichtbare Änderung durchgegangen und hätte beide Paletten dauerhaft
  eingefroren.
- **`Dialog` selbst bauen** mit eigener Fokusfalle (~120 Zeilen). Wäre in jsdom
  vollständig prüfbar gewesen — der Preis wäre, Fokuseinschluss, Esc und
  Inertheit selbst zu verantworten, wo die Plattform sie geschenkt liefert. Die
  bessere Prüfbarkeit hätte hier den schlechteren Dialog gekauft.
- **`Skeleton`, `Dialog`, `Toast` weglassen**, bis eine Route sie verlangt. Wäre
  strenger und näher an der Hausregel; verworfen, weil sie beim Umstellen der 23
  Routen erwartet werden und ihr Fehlen dort zu Handarbeit je Seite führen würde.
  Punkt 6 setzt das Ablaufdatum, damit die Ausnahme keine Regel wird.
