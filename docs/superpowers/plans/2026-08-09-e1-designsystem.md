# E1 — Das Design-System, vollständig: Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `packages/ui` wächst von fünf auf zwanzig Komponenten und von zwei
Paletten auf eine, sodass die 26 Routen in E2/E3 nichts mehr selbst erfinden
müssen — ohne eine einzige Route anzufassen.

**Architecture:** Handgeschriebene Primitives, keine Bibliothek. Wo ein natives
HTML-Element die Zugänglichkeit schon leistet (`<select>`, `<dialog>`,
`<fieldset>`), wird es benutzt statt nachgebaut. Das CSS zieht von einer Datei
in `src/styles/` mit einer Datei je Bauteil, zusammengehalten von einer
Einstiegsdatei aus reinen `@import`-Zeilen. Zwei Wächtertests halten die eine
Palette fest.

**Tech Stack:** React 19, TypeScript (strict, `noUncheckedIndexedAccess`,
`verbatimModuleSyntax`), Vitest 4 + jsdom 30 + Testing Library, handgeschriebenes
CSS mit `--wt-*` Custom Properties, Playwright 1.62 (nur für das
Screenshot-Werkzeug), pnpm + turbo.

**Zweig:** `ui-designsystem` (existiert, liegt auf `origin/develop` 59b0b5d, ein
Commit voraus: die Spezifikation).

**Spezifikation:** `docs/superpowers/specs/2026-08-09-oberflaeche-refactor-design.md`

## Global Constraints

Diese Regeln gelten für **jede** Aufgabe. Sie werden nicht wiederholt.

- **Paketmanager ist `pnpm`** — niemals `npm` oder `yarn`. Python bleibt `uv`.
- **Kein Tailwind, kein Radix, keine Komponentenbibliothek.** Entschieden in
  CLAUDE.md, bleibt so. Keine neue Abhängigkeit in `packages/ui/package.json`.
- **Kein Bauteil, das einen Menschen als Zahl darstellt** (ADR-0022): kein
  Score, kein Prozent, kein Ranking, kein Fortschrittsbalken über Personen.
- **Kein Dark-Mode.** Keine zweite Palette, auch nicht vorbereitend.
- **Die Oberfläche ist deutsch und hartkodiert.** Sichtbare Texte in Bauteilen
  kommen vom Aufrufer, nicht aus dem Bauteil — außer wo dieser Plan es
  ausdrücklich anders sagt (`Dialog` Schließen-Knopf, `Toast` Schließen-Knopf).
  Kein i18n.
- **`packages/ui` importiert nichts aus `apps/web`** — keine Feature-Logik,
  keinen Zustand, keine API-Clients (docs/frontend.md).
- **Testsprache:** `describe`/`it`-Beschreibungen auf Englisch, Inhalte und
  Labels auf Deutsch — so wie `packages/ui/src/components/switch.test.tsx` es
  heute macht.
- **Tests prüfen Verhalten, nicht Klassennamen**: Rolle, zugänglicher Name,
  zugängliche Beschreibung, Tastatur, Fokus. `screen.getByRole(...)` statt
  `container.querySelector(".wt-…")`. Wo eine Ausnahme nötig ist, steht der
  Grund als Kommentar im Test.
- **Keine Route in `apps/web/src/routes/` wird in E1 geändert.** Die **386**
  Testfälle in `apps/web` müssen am Ende unverändert grün sein. Einzige
  erlaubte Ausnahmen: `apps/web/src/styles.css` (Fallbacks entfernen),
  `apps/web/src/styles.test.ts` (neu), `apps/web/package.json` (Skript),
  `apps/web/playwright.shots.config.ts` + `apps/web/e2e-shots/` (neu),
  `.gitignore`.
- **Jede neue Komponente wird in `packages/ui/src/index.ts` exportiert**, Wert
  und Typ getrennt, alphabetisch einsortiert wie heute.
- **Dateinamen kebab-case** (`text-area.tsx`), Komponenten PascalCase.
- **Gate je Aufgabe:** `pnpm --filter @workertransfer/ui run test` und
  `pnpm --filter @workertransfer/ui run check` grün, bevor committet wird.
- **Commits** enden mit `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Nie testen, während ein Docker-Stapel oder ein Build läuft** (Falle 2 im
  Prompt). Für Aufgabe 16 wird der Stapel gebraucht — dann laufen keine
  Unit-Tests nebenher.

## Dateistruktur

```text
packages/ui/
  package.json                      unverändert (keine neue Abhängigkeit)
  src/
    index.ts                        MOD  — 15 neue Exporte
    styles.css                      MOD  — wird zur reinen @import-Liste
    styles.test.ts                  NEU  — Wächter: eine Palette, keine Fallbacks
    styles/
      tokens.css                    NEU  — der einzige Ort mit --wt-*-Definitionen
      base.css                      NEU  — Reset, body, h1-h3, p, a
      button.css   card.css   field.css   switch.css        NEU (aus styles.css gezogen)
      loading.css  alert.css  empty.css   skeleton.css      NEU
      select.css   checkbox.css   fieldset.css              NEU
      page.css     row.css    description-list.css          NEU
      badge.css    visually-hidden.css                      NEU
      dialog.css   toast.css                                NEU
    components/
      loading.tsx  loading.test.tsx
      alert.tsx    alert.test.tsx
      empty.tsx    empty.test.tsx
      live-region.tsx  live-region.test.tsx
      skeleton.tsx     skeleton.test.tsx
      select.tsx       select.test.tsx
      checkbox.tsx     checkbox.test.tsx
      fieldset.tsx     fieldset.test.tsx        (Fieldset + RadioGroup)
      page.tsx         page.test.tsx
      row.tsx          row.test.tsx             (RowList + Row)
      description-list.tsx  description-list.test.tsx
      badge.tsx        badge.test.tsx
      visually-hidden.tsx   visually-hidden.test.tsx
      dialog.tsx       dialog.test.tsx
      toast.tsx        toast.test.tsx
    test/setup.ts                    MOD  — jsdom-Shim für <dialog>

apps/web/
  src/styles.css                     MOD  — 21 Fallbacks entfernt
  src/styles.test.ts                 NEU  — Wächter gegen die Tokens des Pakets
  package.json                       MOD  — Skript "shots"
  playwright.shots.config.ts         NEU
  e2e-shots/oberflaeche.shots.ts     NEU
.gitignore                           MOD  — .screenshots/
docs/adr/0029-designsystem-traegt-die-oberflaeche.md   NEU
docs/frontend.md                     MOD  — Inventar-Abschnitt
docs/ROADMAP.md                      MOD  — Querschnitt-Eintrag
```

**Warum das CSS aufgeteilt wird:** die ganze Begründung dieses Schnitts ist,
dass 885 Zeilen in einer Datei das Problem sind. Zwanzig Bauteile in eine
Datei zu legen würde denselben Fehler in `packages/ui` wiederholen. Eine Datei
je Bauteil liegt neben dem Bauteil, und die Einstiegsdatei bleibt lesbar.

---

### Task 1: Eine Palette, ein Token-Satz, zwei Wächter

Die riskanteste Aufgabe und deshalb die erste: sie verändert die Oberfläche
**sichtbar** an 23 Stellen. Alle folgenden Bauteile bauen auf diesen Tokens auf.

**Files:**
- Create: `packages/ui/src/styles/tokens.css`
- Create: `packages/ui/src/styles/base.css`
- Create: `packages/ui/src/styles/button.css`, `card.css`, `field.css`, `switch.css`
- Create: `packages/ui/src/styles.test.ts`
- Create: `apps/web/src/styles.test.ts`
- Modify: `packages/ui/src/styles.css` (wird reine `@import`-Liste)
- Modify: `apps/web/src/styles.css` (21 Fallbacks entfernen)

**Interfaces:**
- Consumes: nichts.
- Produces: die Token-Namen, die **alle** folgenden Aufgaben benutzen:
  `--wt-ink`, `--wt-muted`, `--wt-surface`, `--wt-surface-raised`,
  `--wt-surface-muted`, `--wt-line`, `--wt-border`, `--wt-mint`, `--wt-green`,
  `--wt-green-deep`, `--wt-accent`, `--wt-danger`, `--wt-danger-soft`,
  `--wt-radius-sm`, `--wt-radius-md`, `--wt-radius-pill`, `--wt-shadow`,
  `--wt-space-1` … `--wt-space-7`, `--wt-text-xs` … `--wt-text-2xl`,
  `--wt-leading-tight`, `--wt-leading-normal`, `--wt-focus-ring`,
  `--wt-disabled-opacity`, `--wt-transition`.

- [ ] **Step 1: Wächtertest für `packages/ui` schreiben (schlägt fehl)**

Create `packages/ui/src/styles.test.ts`:

```ts
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const STYLES_DIR = fileURLToPath(new URL("./styles/", import.meta.url));
const TOKENS_FILE = join(STYLES_DIR, "tokens.css");

function cssTexts(): { name: string; text: string }[] {
  return readdirSync(STYLES_DIR)
    .filter((name) => name.endsWith(".css"))
    .map((name) => ({ name, text: readFileSync(join(STYLES_DIR, name), "utf8") }));
}

export function definedTokens(text: string): Set<string> {
  const found = new Set<string>();
  for (const match of text.matchAll(/(--wt-[a-z0-9-]+)\s*:/g)) found.add(match[1]!);
  return found;
}

export function tokenUsages(text: string): { token: string; hasFallback: boolean }[] {
  const uses: { token: string; hasFallback: boolean }[] = [];
  for (const match of text.matchAll(/var\(\s*(--wt-[a-z0-9-]+)\s*(,)?/g)) {
    uses.push({ token: match[1]!, hasFallback: match[2] === "," });
  }
  return uses;
}

describe("die Palette des Design-Systems", () => {
  // Zuerst: der Wächter muss überhaupt etwas lesen. Ohne diese Prüfung
  // schrumpft er bei einer Umbenennung des Ordners still auf null Vergleiche
  // und meldet „alles in Ordnung" über Dateien, die er nicht mehr findet —
  // dieselbe Fehlerklasse wie ein E2E-Lauf, der sich überspringt und grün
  // berichtet. Dasselbe Muster wie in tests/test_roadmap_status_is_consistent.py.
  it("reads the token file and the component styles at all", () => {
    const files = cssTexts();
    const tokens = definedTokens(readFileSync(TOKENS_FILE, "utf8"));

    expect(files.length).toBeGreaterThanOrEqual(6);
    expect(tokens.size).toBeGreaterThanOrEqual(25);
    expect(files.flatMap((file) => tokenUsages(file.text)).length).toBeGreaterThanOrEqual(20);
  });

  it("defines every token it uses", () => {
    const tokens = definedTokens(readFileSync(TOKENS_FILE, "utf8"));

    const undefinedUses = cssTexts().flatMap((file) =>
      tokenUsages(file.text)
        .filter((use) => !tokens.has(use.token))
        .map((use) => `${file.name}: ${use.token}`)
    );

    expect(undefinedUses).toEqual([]);
  });

  // Ein Fallback ist genau der Weg, auf dem hier zwei Paletten entstanden sind:
  // var(--wt-accent, #1f6f5c) sah aus wie ein Token und war ein zweiter Grünton.
  // Ist das Token definiert, ist der Fallback toter Code mit einer Meinung.
  it("uses no fallback values, because a fallback is a second palette", () => {
    const withFallback = cssTexts().flatMap((file) =>
      tokenUsages(file.text)
        .filter((use) => use.hasFallback)
        .map((use) => `${file.name}: ${use.token}`)
    );

    expect(withFallback).toEqual([]);
  });

  it("keeps all token definitions in one file", () => {
    const strays = cssTexts()
      .filter((file) => file.name !== "tokens.css")
      .filter((file) => definedTokens(file.text).size > 0)
      .map((file) => file.name);

    expect(strays).toEqual([]);
  });
});
```

- [ ] **Step 2: Test laufen lassen — er muss scheitern**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/styles.test.ts`
Expected: FAIL — `ENOENT: no such file or directory, .../src/styles/` (der Ordner
existiert noch nicht).

- [ ] **Step 3: `tokens.css` schreiben**

Create `packages/ui/src/styles/tokens.css`:

```css
/* Der EINZIGE Ort, an dem eine --wt-*-Variable definiert wird.
   src/styles.test.ts hält das fest.

   Vorgeschichte: --wt-accent, --wt-border und --wt-surface-muted wurden an 21
   Stellen in apps/web/src/styles.css und an 2 Stellen im Switch benutzt, ohne
   je definiert zu sein. Es griffen die Fallbacks, und die waren eine zweite
   Palette — #1f6f5c gegen --wt-green #1b6a47, #d6d3cd gegen --wt-line #dbe4db.
   Die Oberfläche hatte damit zwei Grüntöne, je nachdem welche Datei ein
   Element gestylt hatte. */
:root {
  /* Farbrollen */
  --wt-ink: #18221d;
  --wt-muted: #607067;
  --wt-surface: #fbfcf8;
  --wt-surface-raised: #ffffff;
  /* Abgesetzte Fläche. Abgeleitet statt aus der Fremdpalette übernommen
     (#f4f1ec war ein warmes Beige in einem kühlen Grün-Grau). */
  --wt-surface-muted: color-mix(in srgb, var(--wt-surface), var(--wt-ink) 5%);
  --wt-line: #dbe4db;
  --wt-mint: #b9f5cf;
  --wt-green: #1b6a47;
  --wt-green-deep: #105137;
  --wt-danger: #b4392f;
  --wt-danger-soft: color-mix(in srgb, var(--wt-danger), transparent 78%);

  /* Aliase, weil beide Namen im Bestand benutzt werden. Ein Alias, kein
     zweiter Wert — genau das war der Fehler. */
  --wt-accent: var(--wt-green);
  --wt-border: var(--wt-line);

  /* Abstände. Die Werte sind die, die im Bestand mehrfach hart dastanden. */
  --wt-space-1: 0.25rem;
  --wt-space-2: 0.4rem;
  --wt-space-3: 0.6rem;
  --wt-space-4: 0.85rem;
  --wt-space-5: 1.2rem;
  --wt-space-6: 1.8rem;
  --wt-space-7: 2.6rem;

  /* Schrift */
  --wt-text-xs: 0.72rem;
  --wt-text-sm: 0.8rem;
  --wt-text-base: 0.92rem;
  --wt-text-lg: 1rem;
  --wt-text-xl: 1.25rem;
  --wt-text-2xl: 1.6rem;
  --wt-leading-tight: 1.45;
  --wt-leading-normal: 1.55;

  /* Radien */
  --wt-radius-sm: 0.75rem;
  --wt-radius-md: 1.25rem;
  --wt-radius-pill: 999px;

  /* Zustände. Das Fokus-Rezept stand dreimal VERSCHIEDEN im Bestand. */
  --wt-shadow: 0 18px 50px rgba(24, 34, 29, 0.08);
  --wt-focus-ring: 0 0 0 3px color-mix(in srgb, var(--wt-mint), transparent 45%);
  --wt-focus-outline: 3px solid color-mix(in srgb, var(--wt-mint), white 30%);
  --wt-disabled-opacity: 0.55;

  /* Bewegung */
  --wt-transition: 160ms ease;
}
```

- [ ] **Step 4: `base.css` und die vier Bauteil-Dateien anlegen**

Verschiebe den Inhalt der heutigen `packages/ui/src/styles.css` **unverändert
bis auf die Tokens und die Fallbacks** in vier Dateien. `base.css` erhält
`*`, `body`, `button/input/textarea/select { font: inherit }`.

Create `packages/ui/src/styles/base.css`:

```css
* {
  box-sizing: border-box;
}

body {
  margin: 0;
  min-width: 20rem;
  color: var(--wt-ink);
  background: var(--wt-surface);
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
}

button,
input,
textarea,
select {
  font: inherit;
}

/* Es gab keinen solchen Block, obwohl transform animiert wird. Wer Bewegung
   abbestellt hat, bekommt sie ab jetzt nicht. */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Create `packages/ui/src/styles/button.css` — der Block `.wt-button*` aus der
alten Datei (Zeilen 34–75 und 143–150), mit `999px` → `var(--wt-radius-pill)`,
`0.55` → `var(--wt-disabled-opacity)`, `160ms ease` → `var(--wt-transition)`
und dem Fokus-Outline aus `var(--wt-focus-outline)`.

Create `packages/ui/src/styles/card.css` — der Block `.wt-card`.

Create `packages/ui/src/styles/field.css` — die Blöcke `.wt-field*` (Zeilen
84–141 und 152–159), mit `#b4392f` → `var(--wt-danger)`,
`rgba(180, 57, 47, 0.22)` → `var(--wt-danger-soft)` und dem Ring aus
`var(--wt-focus-ring)`.

Create `packages/ui/src/styles/switch.css` — die Blöcke `.wt-switch*` **und**
`.wt-checkbox`, wobei `var(--wt-border, #d6d3cd)` zu `var(--wt-border)` und
`var(--wt-accent, #1f6f5c)` zu `var(--wt-accent)` wird. `.wt-checkbox` wandert
in Aufgabe 8 nach `checkbox.css`; hier bleibt es zunächst, damit dieser Schritt
nichts löscht, was noch benutzt wird.

- [ ] **Step 5: `styles.css` zur Einstiegsdatei machen**

Replace the whole content of `packages/ui/src/styles.css`:

```css
/* Einstiegsdatei. Nur @import-Zeilen — die Reihenfolge ist bedeutungstragend:
   Tokens zuerst, dann das Grundgerüst, dann die Bauteile.

   Aufgeteilt, weil die ganze Begründung dieses Schnitts lautet, dass 885
   Zeilen in einer Datei das Problem sind. Zwanzig Bauteile in eine Datei zu
   legen hätte denselben Fehler hier wiederholt. */
@import "./styles/tokens.css";
@import "./styles/base.css";
@import "./styles/button.css";
@import "./styles/card.css";
@import "./styles/field.css";
@import "./styles/switch.css";
```

- [ ] **Step 6: Wächtertest laufen lassen — er muss bestehen**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/styles.test.ts`
Expected: PASS, 4 Tests.

- [ ] **Step 7: Wächtertest für `apps/web` schreiben (schlägt fehl)**

Create `apps/web/src/styles.test.ts`:

```ts
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

// apps/web hängt von packages/ui ab, nie umgekehrt — deshalb steht diese
// Prüfung hier und nicht im Paket. Sie liest die Definitionen des Pakets und
// die Verwendungen dieser Anwendung.
const OWN_CSS = fileURLToPath(new URL("./styles.css", import.meta.url));
const UI_TOKENS = fileURLToPath(
  new URL("../../../packages/ui/src/styles/tokens.css", import.meta.url)
);

function definedTokens(text: string): Set<string> {
  const found = new Set<string>();
  for (const match of text.matchAll(/(--wt-[a-z0-9-]+)\s*:/g)) found.add(match[1]!);
  return found;
}

function tokenUsages(text: string): { token: string; hasFallback: boolean }[] {
  const uses: { token: string; hasFallback: boolean }[] = [];
  for (const match of text.matchAll(/var\(\s*(--wt-[a-z0-9-]+)\s*(,)?/g)) {
    uses.push({ token: match[1]!, hasFallback: match[2] === "," });
  }
  return uses;
}

describe("apps/web/src/styles.css gegen die Tokens des Design-Systems", () => {
  it("reads both files at all", () => {
    expect(definedTokens(readFileSync(UI_TOKENS, "utf8")).size).toBeGreaterThanOrEqual(25);
    expect(tokenUsages(readFileSync(OWN_CSS, "utf8")).length).toBeGreaterThanOrEqual(20);
  });

  it("uses only tokens the design system defines", () => {
    const tokens = definedTokens(readFileSync(UI_TOKENS, "utf8"));

    const unknown = tokenUsages(readFileSync(OWN_CSS, "utf8"))
      .filter((use) => !tokens.has(use.token))
      .map((use) => use.token);

    expect([...new Set(unknown)]).toEqual([]);
  });

  // 21 Stellen trugen einen Fallback, und die Fallbackwerte waren eine zweite
  // Palette. Solange der Fallback dasteht, kommt sie zurück, sobald jemand ein
  // Token umbenennt — still, denn es sieht ja weiterhin gut aus.
  it("carries no fallback values", () => {
    const withFallback = tokenUsages(readFileSync(OWN_CSS, "utf8"))
      .filter((use) => use.hasFallback)
      .map((use) => use.token);

    expect([...new Set(withFallback)]).toEqual([]);
  });
});
```

- [ ] **Step 8: Test laufen lassen — er muss an den Fallbacks scheitern**

Run: `pnpm --filter @workertransfer/web exec vitest run src/styles.test.ts`
Expected: FAIL im dritten Test — `expected [ '--wt-accent', '--wt-border',
'--wt-surface-muted' ] to deeply equal []`.

- [ ] **Step 9: Die 21 Fallbacks entfernen**

In `apps/web/src/styles.css` jeden `var(--wt-X, …)` durch `var(--wt-X)`
ersetzen. Betroffen sind die Zeilen 454, 463, 503, 544, 569, 585, 601, 621,
650, 664, 701, 759, 774, 782, 816, 833, 841, 843, 855, 878, 880.

```bash
cd /Users/davidozturk/Projects/workertransfer
perl -0pi -e 's/var\(\s*(--wt-[a-z0-9-]+)\s*,[^)]*\)/var($1)/g' apps/web/src/styles.css
grep -cE 'var\(--wt-[a-z0-9-]+,' apps/web/src/styles.css   # muss 0 sein
```

Achtung: `.badge` (Zeile 601) benutzt `var(--wt-accent, #1f6f5c)` als
`background` — nach der Ersetzung ist es `--wt-green`. Das ist eine der drei
beabsichtigten sichtbaren Änderungen.

- [ ] **Step 10: Beide Wächter laufen lassen**

Run: `pnpm --filter @workertransfer/web exec vitest run src/styles.test.ts && pnpm --filter @workertransfer/ui exec vitest run src/styles.test.ts`
Expected: PASS, 3 + 4 Tests.

- [ ] **Step 11: Der Beweis, dass die `@import`-Kette wirklich baut**

Vite löst `@import` in CSS auf, aber das ist hier eine Behauptung und keine
Messung — und ein gebautes Frontend ohne Stylesheet fällt in keinem Unit-Test
auf.

```bash
pnpm --filter @workertransfer/web run build
# Das gebaute CSS muss die Tokens UND die Bauteilregeln enthalten:
grep -c -- '--wt-green' apps/web/dist/assets/*.css   # > 0
grep -c '\.wt-switch__track' apps/web/dist/assets/*.css  # > 0
```
Expected: beide Zahlen > 0. Ist eine 0 dabei, ist die `@import`-Kette nicht
aufgelöst worden und Step 5 muss zurückgenommen werden (dann bleibt eine
einzige `styles.css`, und der Wächter liest sie statt des Ordners).

- [ ] **Step 12: Volles Frontend-Gate**

Run: `pnpm check && pnpm test && pnpm build`
Expected: alle grün. `pnpm test` muss weiterhin die **386** Testfälle aus
`apps/web` melden plus 20 + 7 aus dem Paket.

- [ ] **Step 13: Commit**

```bash
git add packages/ui/src/styles.css packages/ui/src/styles/ packages/ui/src/styles.test.ts \
        apps/web/src/styles.css apps/web/src/styles.test.ts
git commit -m "$(cat <<'MSG'
refactor(ui): eine Palette statt zwei — und ein Wächter dagegen

--wt-accent, --wt-border und --wt-surface-muted wurden an 21 Stellen in
apps/web/src/styles.css und an 2 Stellen im Switch benutzt, ohne je definiert
zu sein. Es griffen die Fallbacks, und die waren eine zweite Palette: #1f6f5c
gegen --wt-green #1b6a47, #d6d3cd gegen --wt-line #dbe4db. Welchen Grünton ein
Element trug, entschied also die Datei, in der es gestylt wurde.

Die beiden Namen werden jetzt Aliase auf die vorhandenen Tokens, nicht zweite
Werte. Das verschiebt 23 Stellen sichtbar — beabsichtigt, und der Grund, warum
der Screenshot-Vergleich schon für diesen Schnitt gilt.

Drei sichtbare Änderungen: --wt-accent von #1f6f5c auf #1b6a47, --wt-border von
#d6d3cd auf #dbe4db, --wt-surface-muted von #f4f1ec auf einen aus --wt-surface
abgeleiteten Ton statt eines warmen Beiges in einer kühlen Palette.

Dazu 25 neue Tokens für Werte, die mehrfach hart dastanden — darunter das
Fokus-Rezept, das dreimal VERSCHIEDEN im Bestand stand. Und ein
prefers-reduced-motion-Block, den es nicht gab, obwohl transform animiert wird.

Das CSS zieht in src/styles/ mit einer Datei je Bauteil. Zwanzig Bauteile in
eine Datei zu legen hätte in packages/ui genau den Fehler wiederholt, dessen
Behebung dieser Schnitt ist.

Zwei Wächter halten es fest: kein benutztes Token ohne Definition, kein
Fallback (das war der Tarnweg), alle Definitionen in einer Datei. Beide prüfen
zuerst, dass sie überhaupt etwas lesen — ein Wächter, der bei einer
Umbenennung still auf null Vergleiche schrumpft, meldet Ordnung über Dateien,
die er nicht mehr findet.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
MSG
)"
```

---

### Task 2: `Loading`

21 Dateien behandeln `isPending` selbst. Es gibt keine Ladeanzeige.

**Files:**
- Create: `packages/ui/src/components/loading.tsx`
- Create: `packages/ui/src/components/loading.test.tsx`
- Create: `packages/ui/src/styles/loading.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Consumes: Tokens aus Task 1.
- Produces: `export interface LoadingProps { label: string; className?: string }`
  und `export function Loading(props: LoadingProps)`.

**Der Grund für `label` als Pflichtfeld:** die Ladetexte im Bestand sind
routenspezifisch — „Portfolio wird geladen…", „Freigaben werden geladen…",
„Marktstatus wird geladen…". Ein hartkodiertes „Wird geladen…" kostete über
zwanzig Tests und nähme der Seite die Auskunft, *was* lädt.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/loading.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Loading } from "./loading";

describe("Loading", () => {
  // role="status" und nicht role="alert": ein Ladevorgang unterbricht nicht,
  // er wird berichtet, sobald der Screenreader Luft hat.
  it("reports itself politely as a status, not as an alert", () => {
    render(<Loading label="Portfolio wird geladen…" />);

    expect(screen.getByRole("status")).toHaveTextContent("Portfolio wird geladen…");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  // Das Label kommt vom Aufrufer. Ein hartkodiertes „Wird geladen…" nähme der
  // Seite die Auskunft, WAS lädt — und der Bestand sagt es je Route anders.
  it("says what is loading, not that something is", () => {
    render(<Loading label="Marktstatus wird geladen…" />);

    expect(screen.getByRole("status")).toHaveTextContent("Marktstatus wird geladen…");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/loading.test.tsx`
Expected: FAIL — `Failed to resolve import "./loading"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/loading.tsx`:

```tsx
export interface LoadingProps {
  /**
   * Was lädt — nicht *dass* etwas lädt.
   *
   * Pflichtfeld, weil die Bestandstexte je Route andere sind („Portfolio wird
   * geladen…", „Freigaben werden geladen…"). Ein Standardwert hier würde diese
   * Auskunft einsammeln und wegwerfen.
   */
  label: string;
  className?: string;
}

/**
 * Die Ladeanzeige. `role="status"` und nicht `role="alert"`: ein Ladevorgang
 * unterbricht nicht, er wird berichtet.
 */
export function Loading({ label, className }: LoadingProps) {
  return (
    <p className={["wt-loading", className].filter(Boolean).join(" ")} role="status">
      {label}
    </p>
  );
}
```

Create `packages/ui/src/styles/loading.css`:

```css
.wt-loading {
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-base);
  line-height: var(--wt-leading-tight);
}
```

Add to `packages/ui/src/styles.css` after the `field.css` line:
`@import "./styles/loading.css";`

Add to `packages/ui/src/index.ts`:
```ts
export { Loading } from "./components/loading";
export type { LoadingProps } from "./components/loading";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/loading.tsx packages/ui/src/components/loading.test.tsx \
        packages/ui/src/styles/loading.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Loading — sagt, WAS lädt

21 Dateien behandeln isPending heute selbst, es gab keine Ladeanzeige. Das
Label ist Pflicht und kommt vom Aufrufer: die Bestandstexte sind
routenspezifisch, und ein hartkodiertes \"Wird geladen…\" hätte über zwanzig
Tests gekostet und der Seite die Auskunft genommen, was lädt.

role=\"status\", nicht role=\"alert\" — ein Ladevorgang unterbricht nicht.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: `Alert`

39 Fundstellen setzen `role="alert"` von Hand, über zwanzig davon mit der Klasse
`auth__alert` auf Seiten, die nichts mit Auth zu tun haben.

**Files:**
- Create: `packages/ui/src/components/alert.tsx`, `alert.test.tsx`, `packages/ui/src/styles/alert.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces: `export type AlertVariant = "error" | "notice"`,
  `export interface AlertProps { children: ReactNode; variant?: AlertVariant; className?: string }`,
  `export function Alert(props: AlertProps)`. Standard ist `"error"`, weil
  der Bestand fast überall `role="alert"` benutzt — der Standard soll den
  häufigen Fall treffen, damit eine Umstellung nichts leise verändert.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/alert.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Alert } from "./alert";

describe("Alert", () => {
  // Der Unterschied zwischen den Varianten ist VERHALTEN, nicht Farbe:
  // role="alert" unterbricht den Screenreader, role="status" wartet. Eine
  // Fehlermeldung, die wartet, kommt zu spät; eine Bestätigung, die
  // unterbricht, ist Lärm.
  it("interrupts for an error", () => {
    render(<Alert>Anmeldung fehlgeschlagen</Alert>);

    expect(screen.getByRole("alert")).toHaveTextContent("Anmeldung fehlgeschlagen");
  });

  it("waits its turn for a notice", () => {
    render(<Alert variant="notice">Freigabe erteilt</Alert>);

    expect(screen.getByRole("status")).toHaveTextContent("Freigabe erteilt");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  // Der Standard trifft den häufigen Fall: der Bestand setzt fast überall
  // role="alert". Wäre "notice" der Standard, würde jede vergessene Variante
  // eine Fehlermeldung leise entschärfen.
  it("defaults to the interrupting variant", () => {
    render(<Alert>Etwas ist schiefgegangen</Alert>);

    expect(screen.getByRole("alert")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/alert.test.tsx`
Expected: FAIL — `Failed to resolve import "./alert"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/alert.tsx`:

```tsx
import type { ReactNode } from "react";

export type AlertVariant = "error" | "notice";

export interface AlertProps {
  children: ReactNode;
  /**
   * `error` unterbricht den Screenreader (`role="alert"`), `notice` wartet
   * (`role="status"`). Der Unterschied ist Verhalten und nicht Farbe: eine
   * Fehlermeldung, die wartet, kommt zu spät.
   */
  variant?: AlertVariant;
  className?: string;
}

export function Alert({ children, variant = "error", className }: AlertProps) {
  const classes = ["wt-alert", `wt-alert--${variant}`, className].filter(Boolean).join(" ");

  return (
    <p className={classes} role={variant === "error" ? "alert" : "status"}>
      {children}
    </p>
  );
}
```

Create `packages/ui/src/styles/alert.css`:

```css
.wt-alert {
  margin: 0;
  border-radius: var(--wt-radius-sm);
  padding: var(--wt-space-3) var(--wt-space-4);
  font-size: var(--wt-text-base);
  line-height: var(--wt-leading-tight);
}

.wt-alert--error {
  border: 1px solid var(--wt-danger);
  background: var(--wt-danger-soft);
  color: var(--wt-danger);
  font-weight: 600;
}

.wt-alert--notice {
  border: 1px solid var(--wt-border);
  background: var(--wt-surface-muted);
  color: var(--wt-ink);
}
```

Add `@import "./styles/alert.css";` to `packages/ui/src/styles.css` and the two
export lines to `index.ts` (`Alert`, and the types `AlertProps`, `AlertVariant`).

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/alert.tsx packages/ui/src/components/alert.test.tsx \
        packages/ui/src/styles/alert.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Alert — zwei Varianten, und der Unterschied ist Verhalten

39 Fundstellen setzen role=\"alert\" von Hand, über zwanzig davon mit der Klasse
auth__alert auf Seiten, die nichts mit Auth zu tun haben.

error unterbricht (role=alert), notice wartet (role=status). Standard ist
error, weil der Bestand fast überall unterbricht — wäre notice der Standard,
würde jede vergessene Variante eine Fehlermeldung leise entschärfen.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `Empty`

27 Leerzustands-Sätze stehen im Bestand, jeder anders gebaut.

**Files:**
- Create: `packages/ui/src/components/empty.tsx`, `empty.test.tsx`, `packages/ui/src/styles/empty.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces: `export interface EmptyProps { title: string; hint?: ReactNode; action?: ReactNode; className?: string }`,
  `export function Empty(props: EmptyProps)`.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/empty.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Empty } from "./empty";

describe("Empty", () => {
  it("says what is not there", () => {
    render(<Empty title="Es läuft gerade kein Gespräch." />);

    expect(screen.getByText("Es läuft gerade kein Gespräch.")).toBeInTheDocument();
  });

  // Ein Leerzustand ist KEIN Fehler und KEIN Ladevorgang. Bekäme er
  // role="status", würde jede leere Liste beim Aufbau der Seite vorgelesen.
  it("is neither an alert nor a status", () => {
    render(<Empty title="Noch keine Arbeiten." />);

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("can offer the way out", () => {
    render(
      <Empty
        title="Noch keine Arbeiten."
        hint="Was du hier ablegst, sieht nur, wem du es freigibst."
        action={<a href="/portfolio">Arbeit hinzufügen</a>}
      />
    );

    expect(screen.getByRole("link", { name: "Arbeit hinzufügen" })).toBeInTheDocument();
    expect(
      screen.getByText("Was du hier ablegst, sieht nur, wem du es freigibst.")
    ).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/empty.test.tsx`
Expected: FAIL — `Failed to resolve import "./empty"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/empty.tsx`:

```tsx
import type { ReactNode } from "react";

export interface EmptyProps {
  /** Was nicht da ist — in einem Satz, aus Sicht der Person. */
  title: string;
  /** Warum das in Ordnung ist, oder was es bedeutet. */
  hint?: ReactNode;
  /** Der Weg heraus, falls es einen gibt. */
  action?: ReactNode;
  className?: string;
}

/**
 * „Hier ist noch nichts."
 *
 * Bewusst ohne `role`: ein Leerzustand ist kein Fehler und kein Ladevorgang.
 * Mit `role="status"` würde jede leere Liste beim Aufbau der Seite vorgelesen.
 */
export function Empty({ title, hint, action, className }: EmptyProps) {
  return (
    <div className={["wt-empty", className].filter(Boolean).join(" ")}>
      <p className="wt-empty__title">{title}</p>
      {hint !== undefined ? <p className="wt-empty__hint">{hint}</p> : null}
      {action !== undefined ? <div className="wt-empty__action">{action}</div> : null}
    </div>
  );
}
```

Create `packages/ui/src/styles/empty.css`:

```css
.wt-empty {
  display: grid;
  gap: var(--wt-space-2);
  border: 1px dashed var(--wt-border);
  border-radius: var(--wt-radius-sm);
  padding: var(--wt-space-6);
  background: var(--wt-surface-muted);
  text-align: center;
}

.wt-empty__title {
  margin: 0;
  color: var(--wt-ink);
  font-size: var(--wt-text-lg);
  font-weight: 700;
}

.wt-empty__hint {
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-normal);
}

.wt-empty__action {
  margin-top: var(--wt-space-2);
}
```

Add the `@import` line and the two export lines.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/empty.tsx packages/ui/src/components/empty.test.tsx \
        packages/ui/src/styles/empty.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Empty — 27 Leerzustände, jeder war anders gebaut

Bewusst ohne role: ein Leerzustand ist kein Fehler und kein Ladevorgang. Mit
role=status würde jede leere Liste beim Aufbau der Seite vorgelesen.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: `LiveRegion` und `useAnnounce`

**0× `aria-live` im ganzen Bestand.** Nach einer Mutation („Freigabe erteilt",
„Gespeichert") wird heute nichts angesagt — die 19 `role="status"`-Stellen
sitzen an Ladeanzeigen, nicht an Ergebnissen.

**Files:**
- Create: `packages/ui/src/components/live-region.tsx`, `live-region.test.tsx`, `packages/ui/src/styles/visually-hidden.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  `export interface LiveRegionProps { message: string; className?: string }`,
  `export function LiveRegion(props: LiveRegionProps)`,
  `export function useAnnounce(): { message: string; announce: (text: string) => void }`.

**Die Falle, die dieser Bau löst:** derselbe Text zweimal hintereinander
angesagt ändert den DOM-Knoten nicht — der Screenreader liest ihn beim zweiten
Mal **nicht** vor. „Gespeichert" nach dem zweiten Speichern wäre stumm. Deshalb
hängt `useAnnounce` bei jeder zweiten Ansage ein unsichtbares Zeichen an.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/live-region.test.tsx`:

```tsx
import { render, renderHook, screen } from "@testing-library/react";
import { act } from "react";
import { describe, expect, it } from "vitest";

import { LiveRegion, useAnnounce } from "./live-region";

describe("LiveRegion", () => {
  it("announces politely and stays out of sight", () => {
    render(<LiveRegion message="Freigabe erteilt" />);

    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("Freigabe erteilt");
  });

  it("renders an empty region while there is nothing to say", () => {
    render(<LiveRegion message="" />);

    // Die Region muss von Anfang an im Baum stehen. Wird sie erst mit der
    // Nachricht eingefügt, liest der Screenreader sie nicht vor — eine
    // Live-Region muss existieren, BEVOR sich ihr Inhalt ändert.
    expect(screen.getByRole("status")).toHaveTextContent("");
  });
});

describe("useAnnounce", () => {
  it("hands the text to the region", () => {
    const { result } = renderHook(() => useAnnounce());

    act(() => result.current.announce("Gespeichert"));

    expect(result.current.message).toContain("Gespeichert");
  });

  // Derselbe Text zweimal ändert den Knoten nicht, und dann liest der
  // Screenreader ihn beim zweiten Mal NICHT vor. „Gespeichert" nach dem
  // zweiten Speichern wäre stumm.
  it("changes the node even when the text repeats", () => {
    const { result } = renderHook(() => useAnnounce());

    act(() => result.current.announce("Gespeichert"));
    const first = result.current.message;
    act(() => result.current.announce("Gespeichert"));
    const second = result.current.message;

    expect(second).not.toBe(first);
    expect(second).toContain("Gespeichert");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/live-region.test.tsx`
Expected: FAIL — `Failed to resolve import "./live-region"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/live-region.tsx`:

```tsx
import { useCallback, useState } from "react";

export interface LiveRegionProps {
  /** Leerer String heißt: nichts zu sagen. Die Region bleibt trotzdem stehen. */
  message: string;
  className?: string;
}

/**
 * Eine höfliche Live-Region.
 *
 * Sie muss von Anfang an im Baum stehen: wird sie erst mit der Nachricht
 * eingefügt, liest der Screenreader sie nicht vor — eine Live-Region muss
 * existieren, bevor sich ihr Inhalt ändert.
 */
export function LiveRegion({ message, className }: LiveRegionProps) {
  return (
    <p
      className={["wt-visually-hidden", className].filter(Boolean).join(" ")}
      role="status"
      aria-live="polite"
    >
      {message}
    </p>
  );
}

/** Ein unsichtbares Zeichen — ändert den Text, ohne ihn zu ändern. */
const NONCE = "​";

/**
 * Sagt Ergebnisse an, die man sonst nur sieht.
 *
 * Der Zähler ist nicht Zierde: derselbe Text zweimal hintereinander ändert den
 * DOM-Knoten nicht, und dann bleibt die zweite Ansage stumm. Bei jeder zweiten
 * Ansage hängt deshalb ein Zero-Width-Space an — sichtbar identisch, für den
 * Vorleser eine Änderung.
 */
export function useAnnounce(): { message: string; announce: (text: string) => void } {
  const [state, setState] = useState({ text: "", turn: 0 });

  const announce = useCallback((text: string) => {
    setState((previous) => ({ text, turn: previous.turn + 1 }));
  }, []);

  const message = state.text === "" ? "" : state.turn % 2 === 0 ? state.text : state.text + NONCE;

  return { message, announce };
}
```

Create `packages/ui/src/styles/visually-hidden.css` — der Block
`.wt-visually-hidden` aus `apps/web/src/styles.css` Zeilen 517–528, wörtlich
übernommen. **Die Fassung in `apps/web` bleibt in E1 stehen**, weil `jobs.tsx`
sie noch benutzt und keine Route angefasst wird; die doppelte Definition
verschwindet in E3a.

Add the `@import` line and export `LiveRegion`, `useAnnounce`, `LiveRegionProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/live-region.tsx packages/ui/src/components/live-region.test.tsx \
        packages/ui/src/styles/visually-hidden.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): LiveRegion und useAnnounce — 0x aria-live im Bestand

Nach einer Mutation wird heute nichts angesagt; die 19 role=status-Stellen
sitzen an Ladeanzeigen, nicht an Ergebnissen.

Zwei Fallen sind eingebaut statt umgangen: die Region steht von Anfang an im
Baum (wird sie erst mit der Nachricht eingefügt, liest sie niemand vor), und
bei jeder zweiten Ansage hängt ein Zero-Width-Space an — derselbe Text zweimal
ändert den Knoten sonst nicht, und \"Gespeichert\" nach dem zweiten Speichern
wäre stumm.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: `Skeleton`

**Kein Verbraucher heute.** Gebaut, weil im Gespräch stand, dass der Bedarf
während des Refactorings entsteht. Findet keine Route in E3 einen Grund, fliegt
er wieder raus — dieses Repo hat `worker-ai`, `worker-files` und
`worker-messaging` genau wegen „kein Verbraucher" gelöscht. Die Entscheidung
gehört in den letzten E3-PR.

**Files:**
- Create: `packages/ui/src/components/skeleton.tsx`, `skeleton.test.tsx`, `packages/ui/src/styles/skeleton.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces: `export interface SkeletonProps { lines?: number; className?: string }`,
  `export function Skeleton(props: SkeletonProps)`. Standard `lines = 3`.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/skeleton.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Loading } from "./loading";
import { Skeleton } from "./skeleton";

describe("Skeleton", () => {
  // Ein Skeleton ist Dekoration. Sichtbar OHNE Ansage ist es für einen
  // Screenreader ein stummer Bildschirm, deshalb gehört es aus dem
  // Zugänglichkeitsbaum heraus und die Ansage in eine Live-Region daneben.
  //
  // Hier wird ausnahmsweise ein Attribut geprüft und keine Rolle: aria-hidden
  // IST hier das Verhalten — es gibt keine Rolle, die man abfragen könnte.
  it("stays out of the accessibility tree", () => {
    const { container } = render(<Skeleton />);

    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
  });

  it("adds nothing a screen reader would announce", () => {
    render(
      <>
        <Skeleton />
        <Loading label="Profil wird geladen…" />
      </>
    );

    // Genau eine Ansage: die des Loading. Das Skeleton schweigt.
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status")).toHaveTextContent("Profil wird geladen…");
  });

  it("draws as many bars as asked for", () => {
    const { container } = render(<Skeleton lines={5} />);

    expect(container.querySelectorAll(".wt-skeleton__bar")).toHaveLength(5);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/skeleton.test.tsx`
Expected: FAIL — `Failed to resolve import "./skeleton"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/skeleton.tsx`:

```tsx
export interface SkeletonProps {
  /** Wie viele Balken. Standard: drei. */
  lines?: number;
  className?: string;
}

/**
 * Ein Platzhalter, solange geladen wird — reine Dekoration.
 *
 * `aria-hidden`, und zwar zwingend: ein sichtbares Skeleton ohne Ansage ist
 * für einen Screenreader ein stummer Bildschirm. Es gehört deshalb IMMER neben
 * ein `Loading` oder eine `LiveRegion`, die sagt, was lädt. Kein `role`, kein
 * `aria-label` — dann wäre die Dekoration plötzlich Inhalt.
 *
 * Kein Fortschrittsbalken: dieses Bauteil weiß nicht, wie weit etwas ist, und
 * über eine Person darf es das ohnehin nie sagen (ADR-0022).
 */
export function Skeleton({ lines = 3, className }: SkeletonProps) {
  return (
    <div className={["wt-skeleton", className].filter(Boolean).join(" ")} aria-hidden="true">
      {Array.from({ length: lines }, (_unused, index) => (
        <span className="wt-skeleton__bar" key={index} />
      ))}
    </div>
  );
}
```

Create `packages/ui/src/styles/skeleton.css`:

```css
.wt-skeleton {
  display: grid;
  gap: var(--wt-space-3);
}

.wt-skeleton__bar {
  display: block;
  height: 0.9rem;
  border-radius: var(--wt-radius-pill);
  background: var(--wt-surface-muted);
}

.wt-skeleton__bar:last-child {
  width: 60%;
}

/* Das Pulsieren ist Zierde und gehört bei abbestellter Bewegung weg. Der
   globale prefers-reduced-motion-Block in base.css kürzt Dauern; hier wird die
   Animation ganz abgeschaltet, damit kein 0.01ms-Flackern bleibt. */
@media (prefers-reduced-motion: no-preference) {
  .wt-skeleton__bar {
    animation: wt-skeleton-pulse 1.4s ease-in-out infinite;
  }
}

@keyframes wt-skeleton-pulse {
  50% {
    opacity: 0.55;
  }
}
```

Add the `@import` line and the two export lines.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/skeleton.tsx packages/ui/src/components/skeleton.test.tsx \
        packages/ui/src/styles/skeleton.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Skeleton — Dekoration, und deshalb stumm

Kein Verbraucher heute. Gebaut, weil der Bedarf laut Absprache im Refactoring
entsteht; findet keine Route in E3 einen Grund, fliegt er raus.

aria-hidden ist zwingend: ein sichtbares Skeleton ohne Ansage ist für einen
Screenreader ein stummer Bildschirm. Es gehört immer neben ein Loading. Kein
role, kein aria-label — dann wäre Dekoration plötzlich Inhalt.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: `Select`

Vier rohe `<select>` im Bestand: `app.tsx:209` (CompanySwitcher),
`team.tsx:166`, `jobs.tsx:147`, `company-jobs.tsx:160`.

**Files:**
- Create: `packages/ui/src/components/select.tsx`, `select.test.tsx`, `packages/ui/src/styles/select.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  ```ts
  export type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
    label: ReactNode;
    hint?: ReactNode;
    error?: ReactNode;
    children: ReactNode;
  };
  export function Select(props: SelectProps)
  ```
  Spiegelt `Field` exakt: `useId`, `hintId`, `errorId`, `aria-describedby` mit
  **beiden** IDs, `aria-invalid` bei `error`.

**Warum nativ:** das APG-Muster für eine Combobox verlangt `role="combobox"`,
`aria-controls`, `aria-expanded`, `aria-autocomplete` **und**
`aria-activedescendant`. Keiner der vier Bestandsselects braucht Filtern oder
Autocomplete — es sind Wertwähler. Ein nachgebautes Listenfeld wäre mehr Code
mit weniger Tastaturunterstützung.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/select.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Select } from "./select";

function Options() {
  return (
    <>
      <option value="">Alle</option>
      <option value="remote">Nur remote</option>
      <option value="onsite">Nur vor Ort</option>
    </>
  );
}

describe("Select", () => {
  it("is reachable by its label", () => {
    render(
      <Select label="Arbeitsform" value="" onChange={() => {}}>
        <Options />
      </Select>
    );

    expect(screen.getByRole("combobox", { name: "Arbeitsform" })).toBeInTheDocument();
  });

  // Das ist der Grund für das native Element: Tastaturbedienung kommt vom
  // Browser und muss nicht nachgebaut werden.
  it("can be operated with the keyboard", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Select label="Arbeitsform" value="" onChange={onChange}>
        <Options />
      </Select>
    );

    await user.selectOptions(screen.getByRole("combobox"), "remote");

    expect(onChange).toHaveBeenCalled();
  });

  // Wie bei Field: Hinweis UND Fehler werden verknüpft. aria-describedby
  // ersetzt sonst das eine durch das andere, und dann hört man den Fehler nicht.
  it("links hint and error together, not one instead of the other", () => {
    render(
      <Select
        label="Rolle"
        hint="Administratoren dürfen einladen."
        error="Bitte eine Rolle wählen."
        value=""
        onChange={() => {}}
      >
        <Options />
      </Select>
    );

    const control = screen.getByRole("combobox", { name: "Rolle" });
    expect(control).toHaveAccessibleDescription(
      "Administratoren dürfen einladen. Bitte eine Rolle wählen."
    );
    expect(control).toBeInvalid();
  });

  it("stays valid while there is no error", () => {
    render(
      <Select label="Arbeitsform" value="" onChange={() => {}}>
        <Options />
      </Select>
    );

    expect(screen.getByRole("combobox")).toBeValid();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/select.test.tsx`
Expected: FAIL — `Failed to resolve import "./select"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/select.tsx`:

```tsx
import type { ReactNode, SelectHTMLAttributes } from "react";
import { useId } from "react";

export type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
  label: ReactNode;
  /** Erklärt die Wahl, bevor jemand die falsche trifft. */
  hint?: ReactNode;
  /** Fehlermeldung; setzt zugleich aria-invalid und die Beschreibung. */
  error?: ReactNode;
  /** Die `<option>`-Elemente. */
  children: ReactNode;
};

/**
 * Ein Wertwähler — als **natives** `<select>`.
 *
 * Kein nachgebautes Listenfeld: das APG-Muster für eine Combobox verlangt
 * `role="combobox"`, `aria-controls`, `aria-expanded`, `aria-autocomplete` und
 * `aria-activedescendant`. Keiner der Wähler in dieser Anwendung braucht
 * Filtern oder Autocomplete. Ein hübsches Listenfeld, das man mit der Tastatur
 * nicht bedienen kann, ist schlechter als ein hässliches, das man kann.
 */
export function Select({ label, hint, error, className, children, ...props }: SelectProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  // Beides verknüpfen, damit ein Screenreader Hinweis UND Fehler vorliest —
  // aria-describedby ersetzt sonst das eine durch das andere.
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className={["wt-field", className].filter(Boolean).join(" ")}>
      <label className="wt-field__label" htmlFor={id}>
        {label}
      </label>
      <select
        id={id}
        className="wt-field__input wt-field__input--select"
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy === "" ? undefined : describedBy}
        {...props}
      >
        {children}
      </select>
      {hint ? (
        <p className="wt-field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {error ? (
        <p className="wt-field__error" id={errorId} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
```

Create `packages/ui/src/styles/select.css`:

```css
/* Erbt alles von .wt-field__input. Nur der Pfeil und der Innenabstand rechts
   sind anders — appearance: none, damit der Rahmenradius auf allen Systemen
   derselbe ist. */
.wt-field__input--select {
  appearance: none;
  padding-right: var(--wt-space-6);
  background-image: linear-gradient(45deg, transparent 50%, var(--wt-muted) 50%),
    linear-gradient(135deg, var(--wt-muted) 50%, transparent 50%);
  background-position: right var(--wt-space-4) center, right var(--wt-space-3) center;
  background-size: 0.32rem 0.32rem, 0.32rem 0.32rem;
  background-repeat: no-repeat;
}
```

Add the `@import` line and export `Select`, `SelectProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/select.tsx packages/ui/src/components/select.test.tsx \
        packages/ui/src/styles/select.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Select — nativ, und das ist die Entscheidung

Vier rohe <select> im Bestand. Kein nachgebautes Listenfeld: das APG-Muster
für eine Combobox verlangt role=combobox, aria-controls, aria-expanded,
aria-autocomplete UND aria-activedescendant, und keiner der vier Wähler
braucht Filtern oder Autocomplete. Tastaturbedienung kommt so vom Browser.

Hülle und aria-Verknüpfung sind identisch zu Field, inklusive der Falle, dass
aria-describedby Hinweis durch Fehler ersetzt, wenn man nur eines setzt.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: `Checkbox`

`.wt-checkbox` existiert als CSS-Klasse in `packages/ui` **ohne Komponente**,
und drei Routen benutzen sie mit rohem `<input type="checkbox">`
(`jobs.tsx:392`, `jobs.tsx:396`, `profile.tsx:206`) — fünf rohe Kästchen
insgesamt.

**Files:**
- Create: `packages/ui/src/components/checkbox.tsx`, `checkbox.test.tsx`, `packages/ui/src/styles/checkbox.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`, `packages/ui/src/styles/switch.css` (`.wt-checkbox` zieht um)

**Interfaces:**
- Produces:
  ```ts
  export type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id" | "type"> & {
    label: ReactNode;
    hint?: ReactNode;
  };
  export function Checkbox(props: CheckboxProps)
  ```

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/checkbox.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Checkbox } from "./checkbox";
import { Switch } from "./switch";

describe("Checkbox", () => {
  it("is reachable by its label", () => {
    render(<Checkbox label="Lebenslauf" checked onChange={() => {}} />);

    expect(screen.getByRole("checkbox", { name: "Lebenslauf" })).toBeChecked();
  });

  it("toggles on click", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Checkbox label="Meine Arbeiten" checked={false} onChange={onChange} />);

    await user.click(screen.getByRole("checkbox"));

    expect(onChange).toHaveBeenCalled();
  });

  it("links its hint for a screen reader", () => {
    render(
      <Checkbox
        label="Meine Arbeiten"
        hint="Auch die hochgeladenen Dateien."
        checked={false}
        onChange={() => {}}
      />
    );

    expect(screen.getByRole("checkbox")).toHaveAccessibleDescription(
      "Auch die hochgeladenen Dateien."
    );
  });

  // Die Unterscheidung ist an beiden Enden festgenagelt, weil sie beim
  // „Aufräumen" als Doppelung aussieht und keine ist: eine Checkbox
  // verspricht, dass die Änderung erst mit dem Absenden gilt; ein Switch gilt
  // sofort. Bei einer Einwilligung ist dieser Unterschied nicht kosmetisch.
  it("is a checkbox and not a switch — the promise differs", () => {
    render(
      <>
        <Checkbox label="Lebenslauf" checked={false} onChange={() => {}} />
        <Switch label="Profil freigeben" checked={false} onChange={() => {}} />
      </>
    );

    expect(screen.getByRole("checkbox", { name: "Lebenslauf" })).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: "Profil freigeben" })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Profil freigeben" })).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/checkbox.test.tsx`
Expected: FAIL — `Failed to resolve import "./checkbox"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/checkbox.tsx`:

```tsx
import type { InputHTMLAttributes, ReactNode } from "react";
import { useId } from "react";

export type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id" | "type"> & {
  label: ReactNode;
  hint?: ReactNode;
};

/**
 * Ein Kästchen in einem Formular — gilt mit dem Absenden.
 *
 * Das ist der Unterschied zu `Switch`, und er ist keine Kosmetik: ein `Switch`
 * ist ein `button[role="switch"]` und wirkt SOFORT, eine Checkbox verspricht
 * „gilt, wenn du absendest". Bei einer Einwilligung entscheidet dieses
 * Versprechen, was die Person glaubt getan zu haben. Wer hier eine Checkbox
 * einsetzt, muss ein Absenden anbieten.
 */
export function Checkbox({ label, hint, className, ...props }: CheckboxProps) {
  const id = useId();
  const hintId = `${id}-hint`;

  return (
    <div className={["wt-checkbox", className].filter(Boolean).join(" ")}>
      <input
        id={id}
        className="wt-checkbox__box"
        type="checkbox"
        aria-describedby={hint ? hintId : undefined}
        {...props}
      />
      <label className="wt-checkbox__label" htmlFor={id}>
        {label}
      </label>
      {hint ? (
        <p className="wt-checkbox__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}
```

Create `packages/ui/src/styles/checkbox.css` — der Block `.wt-checkbox` aus
`switch.css` (dort **entfernen**), plus:

```css
.wt-checkbox {
  display: grid;
  grid-template-columns: auto 1fr;
  align-items: center;
  gap: var(--wt-space-1) var(--wt-space-3);
  font-size: var(--wt-text-base);
}

.wt-checkbox__box {
  width: 1.05rem;
  height: 1.05rem;
  accent-color: var(--wt-accent);
}

.wt-checkbox__box:focus-visible {
  outline: var(--wt-focus-outline);
  outline-offset: 2px;
}

.wt-checkbox__label {
  cursor: pointer;
}

/* Der Hinweis steht unter beiden Spalten, damit er am Text ausgerichtet ist
   und nicht am Kästchen. */
.wt-checkbox__hint {
  grid-column: 2;
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-tight);
}
```

**Achtung:** die alte `.wt-checkbox`-Regel (`display: flex; align-items:
center; gap: 0.55rem; font-size: 0.92rem`) wird von drei Routen benutzt, die in
E1 nicht angefasst werden. Die neue Regel muss dieselben Routen weiter
brauchbar darstellen — `display: grid` mit zwei Spalten tut das für
`<label class="wt-checkbox"><input/><span/></label>` ebenfalls. Nach dem
Umschreiben `pnpm --filter @workertransfer/web run build` und einen Blick auf
`/jobs` und `/profile` im Screenshot-Lauf von Aufgabe 16.

Add the `@import` line and export `Checkbox`, `CheckboxProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/checkbox.tsx packages/ui/src/components/checkbox.test.tsx \
        packages/ui/src/styles/checkbox.css packages/ui/src/styles/switch.css \
        packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Checkbox — die CSS-Klasse gab es, die Komponente nicht

.wt-checkbox stand seit je in packages/ui, und drei Routen benutzten sie mit
rohem <input type=checkbox> — fünf Kästchen ohne Komponente.

Ein Test nagelt die Unterscheidung zu Switch an beiden Enden fest, weil sie
beim Aufräumen wie eine Doppelung aussieht und keine ist: eine Checkbox
verspricht \"gilt mit dem Absenden\", ein Switch wirkt sofort. Bei einer
Einwilligung entscheidet dieses Versprechen, was die Person glaubt getan zu
haben.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: `Fieldset` und `RadioGroup`

Drei `<fieldset>` im Bestand (Lebenslauf-Stationen) und genau eine Radiogruppe
(`/markt`, drei Zustände).

**Files:**
- Create: `packages/ui/src/components/fieldset.tsx`, `fieldset.test.tsx`, `packages/ui/src/styles/fieldset.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  ```ts
  export interface FieldsetProps { legend: ReactNode; hint?: ReactNode; children: ReactNode; className?: string }
  export function Fieldset(props: FieldsetProps)
  export interface RadioOption { value: string; label: ReactNode; hint?: ReactNode }
  export interface RadioGroupProps {
    legend: ReactNode; name: string; value: string;
    onChange: (value: string) => void; options: RadioOption[];
    hint?: ReactNode; className?: string;
  }
  export function RadioGroup(props: RadioGroupProps)
  ```

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/fieldset.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Fieldset, RadioGroup } from "./fieldset";

const MARKT: { value: string; label: string; hint: string }[] = [
  { value: "quiet", label: "Ruhig", hint: "Niemand spricht dich an." },
  { value: "open", label: "Offen", hint: "Unternehmen dürfen fragen." },
  { value: "active", label: "Aktiv", hint: "Du suchst gerade." },
];

describe("Fieldset", () => {
  it("names the group with its legend", () => {
    render(
      <Fieldset legend="Station">
        <input aria-label="Arbeitgeber" />
      </Fieldset>
    );

    expect(screen.getByRole("group", { name: "Station" })).toBeInTheDocument();
  });
});

describe("RadioGroup", () => {
  it("names the group and offers one radio per option", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    expect(screen.getByRole("group", { name: "Marktstatus" })).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(3);
    expect(screen.getByRole("radio", { name: /Offen/ })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: /Ruhig/ })).toBeChecked();
  });

  it("reports the chosen value, not the event", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={onChange}
        options={MARKT}
      />
    );

    await user.click(screen.getByRole("radio", { name: /Aktiv/ }));

    expect(onChange).toHaveBeenCalledWith("active");
  });

  // Der gemeinsame `name` ist das, was den Browser die Gruppe bilden lässt —
  // und erst dadurch bewegen die Pfeiltasten den Fokus zwischen den
  // Auswahlknöpfen. Ohne ihn wären es drei einzelne Knöpfe, die gleich
  // aussehen. Die Pfeiltastenbedienung selbst ist die des Browsers; hier wird
  // geprüft, dass sie überhaupt zustande kommt.
  it("groups the radios under one name so the browser wires the arrow keys", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    for (const radio of screen.getAllByRole("radio")) {
      expect(radio).toHaveAttribute("name", "markt");
    }
  });

  it("explains each option, because three words are not three choices", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    expect(screen.getByRole("radio", { name: /Offen/ })).toHaveAccessibleDescription(
      "Unternehmen dürfen fragen."
    );
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/fieldset.test.tsx`
Expected: FAIL — `Failed to resolve import "./fieldset"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/fieldset.tsx`:

```tsx
import type { ReactNode } from "react";
import { useId } from "react";

export interface FieldsetProps {
  legend: ReactNode;
  hint?: ReactNode;
  children: ReactNode;
  className?: string;
}

/**
 * Eine benannte Gruppe von Feldern — natives `<fieldset>` mit `<legend>`.
 *
 * Kein `div` mit `role="group"` und `aria-label`: die Legende ist sichtbarer
 * Text und zugänglicher Name in einem, und genau das will man hier.
 */
export function Fieldset({ legend, hint, children, className }: FieldsetProps) {
  const id = useId();
  const hintId = `${id}-hint`;

  return (
    <fieldset
      className={["wt-fieldset", className].filter(Boolean).join(" ")}
      aria-describedby={hint ? hintId : undefined}
    >
      <legend className="wt-fieldset__legend">{legend}</legend>
      {hint ? (
        <p className="wt-fieldset__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      {children}
    </fieldset>
  );
}

export interface RadioOption {
  value: string;
  label: ReactNode;
  /** Was diese Wahl bedeutet. Drei Wörter sind keine drei Entscheidungen. */
  hint?: ReactNode;
}

export interface RadioGroupProps {
  legend: ReactNode;
  /** Der gemeinsame Name — er ist es, der den Browser die Gruppe bilden lässt. */
  name: string;
  value: string;
  /** Bekommt den gewählten Wert, nicht das Ereignis. */
  onChange: (value: string) => void;
  options: RadioOption[];
  hint?: ReactNode;
  className?: string;
}

/**
 * Eine Auswahl aus wenigen benannten Zuständen.
 *
 * Native `<input type="radio">` mit gemeinsamem `name`: erst dadurch bewegen
 * die Pfeiltasten den Fokus innerhalb der Gruppe, und die Gruppe verhält sich
 * beim Tabben wie ein Element. Nachgebaut wäre beides Handarbeit.
 */
export function RadioGroup({
  legend,
  name,
  value,
  onChange,
  options,
  hint,
  className,
}: RadioGroupProps) {
  const id = useId();

  return (
    <Fieldset
      legend={legend}
      hint={hint}
      className={["wt-radio-group", className].filter(Boolean).join(" ")}
    >
      {options.map((option) => {
        const optionId = `${id}-${option.value}`;
        const hintId = `${optionId}-hint`;
        return (
          <div className="wt-radio" key={option.value}>
            <input
              className="wt-radio__input"
              id={optionId}
              type="radio"
              name={name}
              value={option.value}
              checked={option.value === value}
              aria-describedby={option.hint !== undefined ? hintId : undefined}
              onChange={() => onChange(option.value)}
            />
            <label className="wt-radio__label" htmlFor={optionId}>
              {option.label}
            </label>
            {option.hint !== undefined ? (
              <p className="wt-radio__hint" id={hintId}>
                {option.hint}
              </p>
            ) : null}
          </div>
        );
      })}
    </Fieldset>
  );
}
```

Create `packages/ui/src/styles/fieldset.css`:

```css
.wt-fieldset {
  display: grid;
  gap: var(--wt-space-3);
  border: 1px solid var(--wt-border);
  border-radius: var(--wt-radius-sm);
  padding: var(--wt-space-4);
}

.wt-fieldset__legend {
  padding: 0 var(--wt-space-2);
  color: var(--wt-ink);
  font-size: var(--wt-text-sm);
  font-weight: 700;
}

.wt-fieldset__hint {
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-tight);
}

.wt-radio {
  display: grid;
  grid-template-columns: auto 1fr;
  align-items: center;
  gap: var(--wt-space-1) var(--wt-space-3);
}

.wt-radio__input {
  accent-color: var(--wt-accent);
}

.wt-radio__input:focus-visible {
  outline: var(--wt-focus-outline);
  outline-offset: 2px;
}

.wt-radio__label {
  cursor: pointer;
  font-weight: 600;
}

.wt-radio__hint {
  grid-column: 2;
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-tight);
}
```

Add the `@import` line and export `Fieldset`, `RadioGroup`, `FieldsetProps`,
`RadioGroupProps`, `RadioOption`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/fieldset.tsx packages/ui/src/components/fieldset.test.tsx \
        packages/ui/src/styles/fieldset.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Fieldset und RadioGroup — nativ, wegen der Pfeiltasten

Drei <fieldset> im Bestand und genau eine Radiogruppe (/markt). Beide nativ:
die Legende ist sichtbarer Text und zugänglicher Name in einem, und der
gemeinsame name ist es, der den Browser die Gruppe bilden lässt — erst dadurch
bewegen die Pfeiltasten den Fokus innerhalb der Gruppe und die Gruppe verhält
sich beim Tabben wie ein Element.

onChange bekommt den Wert, nicht das Ereignis. Jede Option kann ihren Satz
tragen: drei Wörter sind keine drei Entscheidungen.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: `Page`

136 gemessene `page*`-Klassennutzungen — der größte einzelne CSS-Block im
Bestand (`.page`, `.page--narrow`, `.page__header`, `.page__lead`,
`.page__note`).

**Files:**
- Create: `packages/ui/src/components/page.tsx`, `page.test.tsx`, `packages/ui/src/styles/page.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  ```ts
  export interface PageProps {
    title: string;
    lead?: ReactNode;
    note?: ReactNode;
    back?: ReactNode;
    narrow?: boolean;
    children: ReactNode;
    className?: string;
  }
  export function Page(props: PageProps)
  ```

**`back` gehört hierher, weil E2.5/E3 Formulare in eigene Routen legen** — und
eine eigene Route braucht einen Weg zurück. Ein `<main>` ohne Rückweg ist auf
einem Deep-Link eine Sackgasse.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/page.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Page } from "./page";

describe("Page", () => {
  it("carries exactly one first-level heading", () => {
    render(
      <Page title="Offene Stellen">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("heading", { level: 1, name: "Offene Stellen" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  // Das <main> trägt den Namen der Seite. Ohne aria-labelledby heißt der
  // Hauptbereich für einen Screenreader nur „main" — bei 26 Seiten hilft das
  // niemandem beim Erkennen, wo er ist.
  it("names its main region after the heading", () => {
    render(
      <Page title="Meine Freigaben">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("main", { name: "Meine Freigaben" })).toBeInTheDocument();
  });

  it("shows lead and note when given", () => {
    render(
      <Page title="Lebenslauf" lead="Wer fragt, bekommt eine Antwort." note="Widerruf jederzeit.">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByText("Wer fragt, bekommt eine Antwort.")).toBeInTheDocument();
    expect(screen.getByText("Widerruf jederzeit.")).toBeInTheDocument();
  });

  // Eigene Routen für Formulare brauchen einen Rückweg — ein <main> ohne ist
  // auf einem Deep-Link eine Sackgasse.
  it("can offer a way back", () => {
    render(
      <Page title="Bewerben" back={<a href="/jobs">Zurück zu den Stellen</a>}>
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("link", { name: "Zurück zu den Stellen" })).toBeInTheDocument();
  });

  it("renders its children", () => {
    render(
      <Page title="Stellen">
        <p>Eine Stelle</p>
      </Page>
    );

    expect(screen.getByText("Eine Stelle")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/page.test.tsx`
Expected: FAIL — `Failed to resolve import "./page"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/page.tsx`:

```tsx
import type { ReactNode } from "react";
import { useId } from "react";

export interface PageProps {
  title: string;
  /** Ein Satz unter der Überschrift: worum es hier geht. */
  lead?: ReactNode;
  /** Nachsatz unter dem Inhalt — meist eine Einschränkung oder ein Verweis. */
  note?: ReactNode;
  /**
   * Der Weg zurück. Gebraucht, sobald ein Formular eine eigene Route hat:
   * ein `<main>` ohne Rückweg ist auf einem Deep-Link eine Sackgasse.
   */
  back?: ReactNode;
  /** Schmale Spalte — für Seiten, die im Kern ein Formular sind. */
  narrow?: boolean;
  children: ReactNode;
  className?: string;
}

/**
 * Das Gerüst einer Seite: `<main>`, eine `<h1>`, optional Vorspann und
 * Nachsatz.
 *
 * Das `<main>` wird über `aria-labelledby` nach der Überschrift benannt — ohne
 * das heißt der Hauptbereich für einen Screenreader nur „main", und bei 26
 * Seiten hilft das niemandem beim Erkennen, wo er ist.
 */
export function Page({ title, lead, note, back, narrow = false, children, className }: PageProps) {
  const headingId = useId();
  const classes = ["wt-page", narrow ? "wt-page--narrow" : null, className]
    .filter(Boolean)
    .join(" ");

  return (
    <main className={classes} aria-labelledby={headingId}>
      <header className="wt-page__header">
        {back !== undefined ? <div className="wt-page__back">{back}</div> : null}
        <h1 className="wt-page__title" id={headingId}>
          {title}
        </h1>
        {lead !== undefined ? <p className="wt-page__lead">{lead}</p> : null}
      </header>
      {children}
      {note !== undefined ? <p className="wt-page__note">{note}</p> : null}
    </main>
  );
}
```

Create `packages/ui/src/styles/page.css`:

```css
.wt-page {
  display: grid;
  gap: var(--wt-space-5);
  width: 100%;
  max-width: 64rem;
  margin: 0 auto;
  padding: var(--wt-space-7) var(--wt-space-5);
}

/* Schmal, weil es im Kern um ein Formular geht. */
.wt-page--narrow {
  max-width: 34rem;
}

.wt-page__header {
  display: grid;
  gap: var(--wt-space-2);
}

.wt-page__back {
  font-size: var(--wt-text-sm);
}

.wt-page__title {
  margin: 0;
  font-size: var(--wt-text-2xl);
  letter-spacing: -0.02em;
}

.wt-page__lead {
  margin: 0;
  max-width: 46rem;
  color: var(--wt-muted);
  font-size: var(--wt-text-lg);
  line-height: var(--wt-leading-normal);
}

.wt-page__note {
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-normal);
}
```

Add the `@import` line and export `Page`, `PageProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/page.tsx packages/ui/src/components/page.test.tsx \
        packages/ui/src/styles/page.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Page — der größte CSS-Block des Bestands als Bauteil

136 page*-Klassennutzungen liegen heute in apps/web/src/styles.css.

Das <main> wird über aria-labelledby nach der Überschrift benannt: ohne das
heißt der Hauptbereich für einen Screenreader nur \"main\", und bei 26 Seiten
hilft das niemandem beim Erkennen, wo er ist.

back gehört von Anfang an dazu, weil E2.5/E3 Formulare in eigene Routen legen
— ein <main> ohne Rückweg ist auf einem Deep-Link eine Sackgasse.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: `RowList` und `Row`

`.requests__row` 33×, dazu `.team li` und `.overview li` — dieselbe Zeile mit
Titel, Metazeile und Aktionen, dreimal eigenständig gebaut.

**Files:**
- Create: `packages/ui/src/components/row.tsx`, `row.test.tsx`, `packages/ui/src/styles/row.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  ```ts
  export interface RowListProps { children: ReactNode; className?: string }
  export function RowList(props: RowListProps)
  export interface RowProps { title: ReactNode; meta?: ReactNode; actions?: ReactNode; className?: string }
  export function Row(props: RowProps)
  ```
  `Row` rendert ein `<li>` und gehört deshalb **in** eine `RowList`.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/row.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Row, RowList } from "./row";

describe("RowList und Row", () => {
  // Eine Liste als Liste: ein Screenreader sagt dann „Liste, 3 Einträge" und
  // die Person weiß, wie viel kommt. Drei divs sagen nichts.
  it("is a list with counted items", () => {
    render(
      <RowList>
        <Row title="Bäckerei Kern" />
        <Row title="Stadtwerke" />
        <Row title="Klinikum Nord" />
      </RowList>
    );

    expect(screen.getByRole("list")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });

  it("shows title, meta and actions", () => {
    render(
      <RowList>
        <Row
          title="Bäckerei Kern"
          meta="Angefragt am 3. August"
          actions={<button type="button">Freigeben</button>}
        />
      </RowList>
    );

    expect(screen.getByText("Bäckerei Kern")).toBeInTheDocument();
    expect(screen.getByText("Angefragt am 3. August")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Freigeben" })).toBeInTheDocument();
  });

  it("works without meta and without actions", () => {
    render(
      <RowList>
        <Row title="Nur ein Titel" />
      </RowList>
    );

    expect(screen.getByRole("listitem")).toHaveTextContent("Nur ein Titel");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/row.test.tsx`
Expected: FAIL — `Failed to resolve import "./row"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/row.tsx`:

```tsx
import type { ReactNode } from "react";

export interface RowListProps {
  children: ReactNode;
  className?: string;
}

/**
 * Die Liste um `Row`.
 *
 * Ein echtes `<ul>`: ein Screenreader sagt dann „Liste, 3 Einträge", und die
 * Person weiß, wie viel kommt. Drei `div` sagen nichts.
 */
export function RowList({ children, className }: RowListProps) {
  return <ul className={["wt-row-list", className].filter(Boolean).join(" ")}>{children}</ul>;
}

export interface RowProps {
  title: ReactNode;
  /** Datum, Status, Herkunft — was die Zeile einordnet. */
  meta?: ReactNode;
  /** Knöpfe rechts. Was hier steht, handelt an genau dieser Zeile. */
  actions?: ReactNode;
  className?: string;
}

/** Eine Zeile je Vorgang. Gehört in eine `RowList`, weil sie ein `<li>` ist. */
export function Row({ title, meta, actions, className }: RowProps) {
  return (
    <li className={["wt-row", className].filter(Boolean).join(" ")}>
      <div className="wt-row__body">
        <p className="wt-row__title">{title}</p>
        {meta !== undefined ? <p className="wt-row__meta">{meta}</p> : null}
      </div>
      {actions !== undefined ? <div className="wt-row__actions">{actions}</div> : null}
    </li>
  );
}
```

Create `packages/ui/src/styles/row.css`:

```css
.wt-row-list {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
}

.wt-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--wt-space-3);
  padding: var(--wt-space-4) 0;
  border-bottom: 1px solid var(--wt-border);
}

.wt-row:last-child {
  border-bottom: 0;
}

.wt-row__body {
  display: grid;
  gap: var(--wt-space-1);
  min-width: 12rem;
  flex: 1 1 auto;
}

.wt-row__title {
  margin: 0;
  font-weight: 700;
}

.wt-row__meta {
  margin: 0;
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  line-height: var(--wt-leading-tight);
}

.wt-row__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--wt-space-3);
}
```

Add the `@import` line and export `Row`, `RowList`, `RowProps`, `RowListProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/row.tsx packages/ui/src/components/row.test.tsx \
        packages/ui/src/styles/row.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): RowList und Row — dieselbe Zeile war dreimal gebaut

.requests__row 33x, dazu .team li und .overview li: Titel, Metazeile,
Aktionen.

Ein echtes <ul>, damit ein Screenreader \"Liste, 3 Einträge\" sagt und die
Person weiß, wie viel kommt. Drei divs sagen nichts.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: `DescriptionList` und `VisuallyHidden`

Zwei kleine Primitives, zusammen ein Review wert. `<dl>` steht 2× im Bestand
(Transfer-Angebot), `.wt-visually-hidden` liegt in `apps/web` und ist ein
Primitiv.

**Files:**
- Create: `packages/ui/src/components/description-list.tsx`, `description-list.test.tsx`, `packages/ui/src/styles/description-list.css`
- Create: `packages/ui/src/components/visually-hidden.tsx`, `visually-hidden.test.tsx`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Consumes: `packages/ui/src/styles/visually-hidden.css` aus Task 5.
- Produces:
  ```ts
  export interface DescriptionListItem { term: ReactNode; description: ReactNode }
  export interface DescriptionListProps { items: DescriptionListItem[]; className?: string }
  export function DescriptionList(props: DescriptionListProps)
  export interface VisuallyHiddenProps { children: ReactNode }
  export function VisuallyHidden(props: VisuallyHiddenProps)
  ```

- [ ] **Step 1: Write the failing tests**

Create `packages/ui/src/components/description-list.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { DescriptionList } from "./description-list";

describe("DescriptionList", () => {
  it("shows every term and its description", () => {
    render(
      <DescriptionList
        items={[
          { term: "Gehalt", description: "58.000 €" },
          { term: "Eintritt", description: "1. Oktober" },
        ]}
      />
    );

    expect(screen.getByText("Gehalt")).toBeInTheDocument();
    expect(screen.getByText("58.000 €")).toBeInTheDocument();
    expect(screen.getByText("Eintritt")).toBeInTheDocument();
    expect(screen.getByText("1. Oktober")).toBeInTheDocument();
  });

  // Hier wird ausnahmsweise die Struktur geprüft und keine Rolle: <dl>/<dt>/<dd>
  // haben in ARIA keine abfragbare Rolle, und die ZUORDNUNG ist genau das, was
  // dieses Bauteil leistet. Steht die Beschreibung nicht direkt hinter ihrem
  // Begriff, ist die Liste falsch, auch wenn beide Texte da sind.
  it("puts each description directly after its own term", () => {
    render(
      <DescriptionList
        items={[
          { term: "Gehalt", description: "58.000 €" },
          { term: "Eintritt", description: "1. Oktober" },
        ]}
      />
    );

    const term = screen.getByText("Gehalt");
    expect(term.tagName).toBe("DT");
    expect(term.nextElementSibling?.tagName).toBe("DD");
    expect(term.nextElementSibling).toHaveTextContent("58.000 €");
  });
});
```

Create `packages/ui/src/components/visually-hidden.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { VisuallyHidden } from "./visually-hidden";

describe("VisuallyHidden", () => {
  // Das Gegenteil von aria-hidden, und der Unterschied ist der ganze Zweck:
  // hier bleibt der Text FÜR DEN VORLESER da und verschwindet nur für das
  // Auge. Ein aria-hidden hier würde die Wörter hinter ✓ und ✗ unterschlagen.
  it("keeps its text in the accessibility tree", () => {
    render(
      <p>
        Python
        <VisuallyHidden> (hast du)</VisuallyHidden>
      </p>
    );

    expect(screen.getByText("(hast du)")).toBeInTheDocument();
  });

  it("does not hide from assistive technology", () => {
    const { container } = render(<VisuallyHidden>Nur zum Hören</VisuallyHidden>);

    expect(container.firstElementChild).not.toHaveAttribute("aria-hidden");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/description-list.test.tsx src/components/visually-hidden.test.tsx`
Expected: FAIL — beide Importe lassen sich nicht auflösen.

- [ ] **Step 3: Write minimal implementations**

Create `packages/ui/src/components/description-list.tsx`:

```tsx
import type { ReactNode } from "react";

export interface DescriptionListItem {
  term: ReactNode;
  description: ReactNode;
}

export interface DescriptionListProps {
  items: DescriptionListItem[];
  className?: string;
}

/**
 * Begriff und Wert in Paaren — `<dl>` mit `<dt>`/`<dd>`.
 *
 * Nicht zwei Spalten aus `div`: die Zuordnung von Begriff zu Wert ist genau
 * das, was hier zählt, und ein `<dl>` trägt sie im Markup statt im Layout.
 */
export function DescriptionList({ items, className }: DescriptionListProps) {
  return (
    <dl className={["wt-dl", className].filter(Boolean).join(" ")}>
      {items.map((item, index) => (
        // Der Index als Schlüssel ist hier richtig: die Liste ist eine
        // Momentaufnahme ohne eigene Identität je Zeile, und `term` kann ein
        // beliebiger ReactNode sein, also kein brauchbarer Schlüssel.
        <div className="wt-dl__pair" key={index}>
          <dt className="wt-dl__term">{item.term}</dt>
          <dd className="wt-dl__description">{item.description}</dd>
        </div>
      ))}
    </dl>
  );
}
```

Create `packages/ui/src/components/visually-hidden.tsx`:

```tsx
import type { ReactNode } from "react";

export interface VisuallyHiddenProps {
  children: ReactNode;
}

/**
 * Text, der gelesen, aber nicht gesehen werden soll.
 *
 * Das **Gegenteil** von `aria-hidden`, und der Unterschied ist der ganze
 * Zweck: der Text bleibt für den Vorleser da und verschwindet nur für das Auge
 * — die Wörter hinter ✓ und ✗, ohne die man drei Namen und keinen Unterschied
 * hört.
 */
export function VisuallyHidden({ children }: VisuallyHiddenProps) {
  return <span className="wt-visually-hidden">{children}</span>;
}
```

Create `packages/ui/src/styles/description-list.css`:

```css
.wt-dl {
  margin: 0;
  display: grid;
  gap: var(--wt-space-3);
}

.wt-dl__pair {
  display: grid;
  gap: var(--wt-space-1);
}

.wt-dl__term {
  color: var(--wt-muted);
  font-size: var(--wt-text-sm);
  font-weight: 700;
}

.wt-dl__description {
  margin: 0;
  font-size: var(--wt-text-lg);
}
```

Add the `@import` line for `description-list.css` and export `DescriptionList`,
`VisuallyHidden` plus their three types. `visually-hidden.css` ist bereits aus
Task 5 eingebunden.

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/description-list.tsx packages/ui/src/components/description-list.test.tsx \
        packages/ui/src/components/visually-hidden.tsx packages/ui/src/components/visually-hidden.test.tsx \
        packages/ui/src/styles/description-list.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): DescriptionList und VisuallyHidden

<dl> steht 2x im Bestand (Transfer-Angebot); die Zuordnung von Begriff zu Wert
gehört ins Markup und nicht ins Layout.

VisuallyHidden ist das Gegenteil von aria-hidden, und der Unterschied ist der
ganze Zweck: der Text bleibt für den Vorleser da und verschwindet nur für das
Auge. Die Klasse lag bisher in apps/web, obwohl sie ein Primitiv ist.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 13: `Badge`

Der Anfragezähler in der Kopfzeile (`apps/web/src/resume/pending-badge.tsx`
baut ihn heute selbst, `.badge` liegt in `apps/web/src/styles.css`). Eigene
Aufgabe, weil dieses Bauteil eine Regel trägt.

**Files:**
- Create: `packages/ui/src/components/badge.tsx`, `badge.test.tsx`, `packages/ui/src/styles/badge.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces: `export interface BadgeProps { count: number; label: string; className?: string }`,
  `export function Badge(props: BadgeProps)`. Gibt `null` zurück, wenn
  `count <= 0`.

**Die Regel (ADR-0022):** ein Zähler zählt **Vorgänge**, niemals Personen. „3
offene Anfragen" ist erlaubt; „Profil 60 % vollständig" oder „Rang 4 von 12"
ist es nicht. Deshalb heißt das Pflichtfeld `label` und nicht `title`: es muss
ein Substantiv im Plural sein, das den Vorgang benennt.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/badge.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Badge } from "./badge";

describe("Badge", () => {
  // Eine nackte Zahl ist für einen Vorleser bedeutungslos: man hört „3" und
  // weiß nicht, drei von was. Das Label liefert das Substantiv.
  it("says what it counts, not just how many", () => {
    render(<Badge count={3} label="3 offene Anfragen" />);

    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByLabelText("3 offene Anfragen")).toBeInTheDocument();
  });

  // Eine Null ist keine Nachricht. Ein Zähler, der 0 zeigt, ist ein
  // Aufmerksamkeitsanspruch ohne Anlass.
  it("shows nothing at zero", () => {
    const { container } = render(<Badge count={0} label="0 offene Anfragen" />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows nothing for a negative count", () => {
    const { container } = render(<Badge count={-1} label="kaputt" />);

    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/badge.test.tsx`
Expected: FAIL — `Failed to resolve import "./badge"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/badge.tsx`:

```tsx
export interface BadgeProps {
  /** Wie viele Vorgänge warten. Bei 0 oder weniger zeigt das Bauteil nichts. */
  count: number;
  /**
   * Der ganze Satz, den ein Vorleser sagen soll — „3 offene Anfragen".
   *
   * Pflichtfeld: eine nackte Zahl ist für einen Vorleser bedeutungslos, man
   * hört „3" und weiß nicht, drei von was.
   */
  label: string;
  className?: string;
}

/**
 * Ein Zähler an einem Navigationslink.
 *
 * Klein, aber nicht dekorativ: er ist heute der einzige Weg, auf dem eine
 * Anfrage die Person erreicht.
 *
 * **Er zählt Vorgänge, niemals Personen** (ADR-0022). „3 offene Anfragen" ist
 * erlaubt. „Profil 60 % vollständig", „Rang 4 von 12" oder irgendeine Zahl
 * über einen Menschen sind es nicht — dafür ist dieses Bauteil nicht da und
 * darf es nicht werden.
 */
export function Badge({ count, label, className }: BadgeProps) {
  // Eine Null ist keine Nachricht, sondern ein Aufmerksamkeitsanspruch ohne
  // Anlass.
  if (count <= 0) return null;

  return (
    <span className={["wt-badge", className].filter(Boolean).join(" ")} aria-label={label}>
      {count}
    </span>
  );
}
```

Create `packages/ui/src/styles/badge.css` — der Block `.badge` aus
`apps/web/src/styles.css` (Zeilen 592–607), umbenannt auf `.wt-badge`, mit
Tokens statt harter Werte:

```css
/* Zähler an einem Navigationslink. Klein, aber nicht dekorativ: er ist der
   einzige Weg, auf dem eine Anfrage die Person heute erreicht. */
.wt-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.25rem;
  height: 1.25rem;
  margin-left: var(--wt-space-2);
  padding: 0 var(--wt-space-1);
  border-radius: var(--wt-radius-pill);
  background: var(--wt-accent);
  color: #fff;
  font-size: var(--wt-text-xs);
  font-weight: 700;
  line-height: 1;
}
```

**`.badge` in `apps/web/src/styles.css` bleibt in E1 stehen**, weil
`pending-badge.tsx` es noch benutzt und E1 keine Route und kein
Feature-Modul anfasst. Die Doppelung verschwindet in E3e mit der Schale.

Add the `@import` line and export `Badge`, `BadgeProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/badge.tsx packages/ui/src/components/badge.test.tsx \
        packages/ui/src/styles/badge.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Badge — zählt Vorgänge, niemals Personen

Der Anfragezähler baut sich heute in resume/pending-badge.tsx selbst.

Zwei Regeln stecken im Bauteil. Das Label ist Pflicht, weil eine nackte Zahl
für einen Vorleser bedeutungslos ist: man hört \"3\" und weiß nicht, drei von
was. Und bei 0 zeigt es nichts — eine Null ist keine Nachricht, sondern ein
Aufmerksamkeitsanspruch ohne Anlass.

Dazu die ADR-0022-Grenze im Docstring: \"3 offene Anfragen\" ja, \"Profil 60 %
vollständig\" oder \"Rang 4 von 12\" nie.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 14: `Dialog`

**Kein Verbraucher heute** (0× `<dialog>`, 0× `window.confirm` im Bestand).
Gebaut, weil im Gespräch stand, dass der Bedarf im Refactoring entsteht.

**Files:**
- Create: `packages/ui/src/components/dialog.tsx`, `dialog.test.tsx`, `packages/ui/src/styles/dialog.css`
- Modify: `packages/ui/src/test/setup.ts` (jsdom-Shim), `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Produces:
  ```ts
  export interface DialogProps {
    open: boolean;
    onClose: () => void;
    title: string;
    children: ReactNode;
    closeLabel?: string;   // Standard: "Schließen"
    className?: string;
  }
  export function Dialog(props: DialogProps)
  ```

**Was der Browser liefert und was wir schreiben.** `showModal()` bringt
Fokuseinschluss, Esc schließt, inerter Hintergrund, `aria-modal="true"`,
Top-Layer und `::backdrop` mit. Selbst zu schreiben bleiben: Fokus zurück zum
auslösenden Element, Anfangsfokus über `autofocus`, `aria-labelledby` auf die
Überschrift, ein echter Schließen-Knopf. `role="dialog"` und `aria-modal`
werden **nicht** von Hand gesetzt — `showModal()` setzt sie schon, und beides
doppelt zu setzen ist der häufigste Fehler an diesem Element.

> **Beweisgrenze, die im PR stehen muss:** jsdom implementiert
> `HTMLDialogElement.showModal()` nicht (jsdom-Issue #3294). Die **Fokusfalle
> und Esc sind in diesem Schnitt nicht bewiesen** — der jsdom-Test prüft nur
> unsere vier eigenen Pflichten. Der Beweis in echtem Chromium kommt mit dem
> ersten Verbraucher in E3, weil E1 keine Route anlegt, die man öffnen könnte.
> Nicht behaupten, was nicht geprüft ist.

- [ ] **Step 1: jsdom-Shim in das Test-Setup schreiben**

Modify `packages/ui/src/test/setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";

/**
 * jsdom kennt `HTMLDialogElement.showModal()` nicht (jsdom-Issue #3294): das
 * Element steht im Baum, bekommt aber kein `open`, und alles, was daran hängt,
 * lässt sich nicht prüfen.
 *
 * Der Ersatz setzt genau das `open`-Attribut und löst beim Schließen das
 * `close`-Ereignis aus — nicht mehr. Er ahmt insbesondere **nicht** die
 * Fokusfalle, Esc oder den inerten Hintergrund nach: das wären dann unsere
 * Behauptungen über unseren eigenen Ersatz und kein Beweis über den Browser.
 * Diese drei Dinge liefert die Plattform und werden in echtem Chromium
 * geprüft, sobald ein Dialog einen Verbraucher hat.
 */
if (typeof HTMLDialogElement !== "undefined") {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.setAttribute("open", "");
  };
  HTMLDialogElement.prototype.show = function show(this: HTMLDialogElement) {
    this.setAttribute("open", "");
  };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    if (!this.hasAttribute("open")) return;
    this.removeAttribute("open");
    this.dispatchEvent(new Event("close"));
  };
}
```

- [ ] **Step 2: Write the failing test**

Create `packages/ui/src/components/dialog.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { Dialog } from "./dialog";

describe("Dialog", () => {
  it("stays shut while closed", () => {
    render(
      <Dialog open={false} onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    expect(screen.queryByRole("dialog")).toBeNull();
  });

  // aria-labelledby ist unsere Pflicht: showModal() setzt role und aria-modal,
  // aber den Namen nicht. Ohne ihn heißt der Dialog für einen Vorleser nur
  // „Dialog".
  it("is named after its heading", () => {
    render(
      <Dialog open onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    expect(screen.getByRole("dialog", { name: "Wirklich löschen?" })).toBeInTheDocument();
  });

  // Ein Dialog, den man nur mit Esc verlassen kann, ist für jemanden ohne
  // Tastatur eine Falle. Der Knopf ist die verlässliche Tür.
  it("always offers an explicit way out", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Dialog open onClose={onClose} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    await user.click(screen.getByRole("button", { name: "Schließen" }));

    expect(onClose).toHaveBeenCalled();
  });

  // Das `close`-Ereignis ist der Weg, auf dem auch Esc ankommt: der Browser
  // schließt selbst und meldet es. Wer nur den Knopf verdrahtet, verliert den
  // Zustand, sobald jemand Esc drückt — die Anwendung glaubt dann, der Dialog
  // sei noch offen.
  it("reports a close that the platform performed", () => {
    const onClose = vi.fn();
    render(
      <Dialog open onClose={onClose} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    const dialog = screen.getByRole("dialog");
    dialog.dispatchEvent(new Event("close"));

    expect(onClose).toHaveBeenCalled();
  });

  // Fokus zurück zum Auslöser: sonst steht der Fokus nach dem Schließen am
  // Seitenanfang und man tabbt sich zurück zu der Stelle, an der man war.
  it("gives focus back to the element that opened it", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Löschen
          </button>
          <Dialog open={open} onClose={() => setOpen(false)} title="Wirklich löschen?">
            <p>Das lässt sich nicht zurücknehmen.</p>
          </Dialog>
        </>
      );
    }

    render(<Harness />);
    const trigger = screen.getByRole("button", { name: "Löschen" });
    await user.click(trigger);
    await user.click(screen.getByRole("button", { name: "Schließen" }));

    expect(trigger).toHaveFocus();
  });

  // showModal() setzt role="dialog" und aria-modal="true" selbst. Beides von
  // Hand zu setzen ist der häufigste Fehler an diesem Element und kann die
  // Ausgabe für Vorleser verdoppeln.
  it("does not set role or aria-modal by hand", () => {
    render(
      <Dialog open onClose={() => {}} title="Wirklich löschen?">
        <p>Das lässt sich nicht zurücknehmen.</p>
      </Dialog>
    );

    const dialog = screen.getByRole("dialog");
    expect(dialog).not.toHaveAttribute("role");
    expect(dialog).not.toHaveAttribute("aria-modal");
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/dialog.test.tsx`
Expected: FAIL — `Failed to resolve import "./dialog"`.

- [ ] **Step 4: Write minimal implementation**

Create `packages/ui/src/components/dialog.tsx`:

```tsx
import type { ReactNode } from "react";
import { useEffect, useId, useRef } from "react";

export interface DialogProps {
  open: boolean;
  /**
   * Wird gerufen, wenn der Dialog geschlossen wurde — durch den Knopf **oder**
   * durch Esc. Der Aufrufer setzt daraufhin `open` auf `false`.
   */
  onClose: () => void;
  title: string;
  children: ReactNode;
  /** Text des Schließen-Knopfes. */
  closeLabel?: string;
  className?: string;
}

/**
 * Ein modaler Dialog — auf dem **nativen** `<dialog>` mit `showModal()`.
 *
 * Vom Browser kommen: Fokuseinschluss, Esc schließt, inerter Hintergrund,
 * `aria-modal="true"`, Top-Layer und `::backdrop`. Selbst geschrieben sind nur
 * die vier Dinge, die die Plattform nicht übernimmt: Fokus zurück zum
 * auslösenden Element, Anfangsfokus, `aria-labelledby`, ein echter
 * Schließen-Knopf.
 *
 * `role="dialog"` und `aria-modal` werden absichtlich NICHT gesetzt —
 * `showModal()` setzt sie, und beides doppelt zu setzen ist der häufigste
 * Fehler an diesem Element.
 *
 * ACHTUNG beim Testen: jsdom implementiert `showModal()` nicht (Issue #3294).
 * `src/test/setup.ts` ersetzt nur `open` und das `close`-Ereignis. Fokusfalle
 * und Esc sind in jsdom deshalb NICHT geprüft; sie gehören in eine
 * Browser-Prüfung.
 */
export function Dialog({
  open,
  onClose,
  title,
  children,
  closeLabel = "Schließen",
  className,
}: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const headingId = useId();
  // Wer den Dialog geöffnet hat. Ohne diese Merkung steht der Fokus nach dem
  // Schließen am Seitenanfang, und man tabbt sich zurück zu der Stelle, an
  // der man war.
  const opener = useRef<Element | null>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (dialog === null) return;

    if (open && !dialog.hasAttribute("open")) {
      opener.current = document.activeElement;
      dialog.showModal();
    } else if (!open && dialog.hasAttribute("open")) {
      dialog.close();
    }
  }, [open]);

  useEffect(() => {
    const dialog = ref.current;
    if (dialog === null) return;

    // Esc kommt hier an, nicht am Knopf: der Browser schließt selbst und
    // meldet es über `close`. Wer nur den Knopf verdrahtet, verliert den
    // Zustand, sobald jemand Esc drückt.
    function handleClose() {
      const previous = opener.current;
      opener.current = null;
      if (previous instanceof HTMLElement) previous.focus();
      onClose();
    }

    dialog.addEventListener("close", handleClose);
    return () => dialog.removeEventListener("close", handleClose);
  }, [onClose]);

  return (
    <dialog
      className={["wt-dialog", className].filter(Boolean).join(" ")}
      ref={ref}
      aria-labelledby={headingId}
    >
      <h2 className="wt-dialog__title" id={headingId}>
        {title}
      </h2>
      <div className="wt-dialog__body">{children}</div>
      {/* Ein Dialog, den man nur mit Esc verlassen kann, ist für jemanden ohne
          Tastatur eine Falle. autofocus setzt den Anfangsfokus auf die
          verlässliche Tür statt irgendwohin. */}
      <button
        type="button"
        className="wt-button wt-button--quiet wt-dialog__close"
        // eslint-disable-line -- autofocus ist hier richtig: der Fokus MUSS in
        // den Dialog wandern, und der Browser wählte sonst das erste
        // fokussierbare Element, was auch ein zerstörender Knopf sein kann.
        autoFocus
        onClick={() => ref.current?.close()}
      >
        {closeLabel}
      </button>
    </dialog>
  );
}
```

Create `packages/ui/src/styles/dialog.css`:

```css
.wt-dialog {
  max-width: 32rem;
  border: 1px solid var(--wt-border);
  border-radius: var(--wt-radius-md);
  padding: var(--wt-space-6);
  background: var(--wt-surface-raised);
  color: var(--wt-ink);
  box-shadow: var(--wt-shadow);
}

.wt-dialog::backdrop {
  background: color-mix(in srgb, var(--wt-ink), transparent 45%);
}

.wt-dialog__title {
  margin: 0 0 var(--wt-space-3);
  font-size: var(--wt-text-xl);
}

.wt-dialog__body {
  display: grid;
  gap: var(--wt-space-3);
  line-height: var(--wt-leading-normal);
}

.wt-dialog__close {
  margin-top: var(--wt-space-5);
}
```

Add the `@import` line and export `Dialog`, `DialogProps`.

- [ ] **Step 5: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS, 6 Dialog-Tests.

Bleibt der Test „gives focus back" rot, liegt es am Shim: `close()` muss das
`close`-Ereignis auslösen. Nicht die Behauptung abschwächen — den Shim
richtigstellen.

- [ ] **Step 6: Commit**

```bash
git add packages/ui/src/components/dialog.tsx packages/ui/src/components/dialog.test.tsx \
        packages/ui/src/styles/dialog.css packages/ui/src/test/setup.ts \
        packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Dialog — nativ, und die Beweisgrenze steht dabei

Kein Verbraucher heute (0x <dialog>, 0x window.confirm). Gebaut, weil der
Bedarf laut Absprache im Refactoring entsteht.

showModal() liefert Fokuseinschluss, Esc, inerten Hintergrund, aria-modal,
Top-Layer und ::backdrop vom Browser. Selbst geschrieben sind nur die vier
Dinge, die die Plattform nicht übernimmt: Fokus zurück zum Auslöser,
Anfangsfokus, aria-labelledby, ein echter Schließen-Knopf. role und aria-modal
werden absichtlich NICHT von Hand gesetzt — das ist der häufigste Fehler an
diesem Element.

Esc wird über das close-Ereignis verdrahtet, nicht über den Knopf: wer nur den
Knopf verdrahtet, verliert den Zustand, sobald jemand Esc drückt, und die
Anwendung glaubt weiter, der Dialog sei offen.

BEWEISGRENZE: jsdom implementiert showModal() nicht (Issue #3294). Der Shim im
Test-Setup ersetzt NUR das open-Attribut und das close-Ereignis und ahmt die
Fokusfalle bewusst nicht nach — das wären Behauptungen über unseren eigenen
Ersatz. Fokusfalle und Esc sind in diesem Schnitt also NICHT bewiesen; der
Beweis in echtem Chromium kommt mit dem ersten Verbraucher in E3.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 15: `Toast`

**Kein Verbraucher heute.** Gebaut wie `Skeleton` und `Dialog`, mit einem
Vorbehalt im Bauteil.

**Files:**
- Create: `packages/ui/src/components/toast.tsx`, `toast.test.tsx`, `packages/ui/src/styles/toast.css`
- Modify: `packages/ui/src/index.ts`, `packages/ui/src/styles.css`

**Interfaces:**
- Consumes: `LiveRegion` aus Task 5.
- Produces:
  ```ts
  export interface ToastProps {
    message: string;
    onDismiss: () => void;
    durationMs?: number;   // ohne Angabe: bleibt stehen
    dismissLabel?: string; // Standard: "Ausblenden"
    className?: string;
  }
  export function Toast(props: ToastProps)
  ```

**Warum es kein automatisches Verschwinden als Standard gibt:** eine Meldung,
die von selbst geht, ist schlechter als eine, die stehen bleibt, sobald jemand
danach handeln muss. Und die Zusage aus ADR-0027 §6 („läuft") darf **niemals**
ein Toast werden — die muss auf der Seite stehen.

- [ ] **Step 1: Write the failing test**

Create `packages/ui/src/components/toast.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { Toast } from "./toast";

describe("Toast", () => {
  it("announces itself politely", () => {
    render(<Toast message="Gespeichert." onDismiss={() => {}} />);

    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("Gespeichert.");
  });

  it("can always be dismissed by hand", async () => {
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    render(<Toast message="Gespeichert." onDismiss={onDismiss} />);

    await user.click(screen.getByRole("button", { name: "Ausblenden" }));

    expect(onDismiss).toHaveBeenCalled();
  });

  describe("mit Zeitgrenze", () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    // Kein automatisches Verschwinden als Standard: eine Meldung, die von
    // selbst geht, ist schlechter als eine, die stehen bleibt, sobald jemand
    // danach handeln muss.
    it("stays until dismissed when no duration is given", () => {
      const onDismiss = vi.fn();
      render(<Toast message="Gespeichert." onDismiss={onDismiss} />);

      vi.advanceTimersByTime(60_000);

      expect(onDismiss).not.toHaveBeenCalled();
    });

    it("goes on its own only when asked to", () => {
      const onDismiss = vi.fn();
      render(<Toast message="Gespeichert." onDismiss={onDismiss} durationMs={4000} />);

      expect(onDismiss).not.toHaveBeenCalled();
      vi.advanceTimersByTime(4000);
      expect(onDismiss).toHaveBeenCalledTimes(1);
    });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --filter @workertransfer/ui exec vitest run src/components/toast.test.tsx`
Expected: FAIL — `Failed to resolve import "./toast"`.

- [ ] **Step 3: Write minimal implementation**

Create `packages/ui/src/components/toast.tsx`:

```tsx
import { useEffect } from "react";

export interface ToastProps {
  message: string;
  onDismiss: () => void;
  /**
   * Nach dieser Zeit verschwindet die Meldung von selbst.
   *
   * **Ohne Angabe bleibt sie stehen**, und das ist der Standard mit Absicht:
   * eine Meldung, die von selbst geht, ist schlechter als eine, die bleibt,
   * sobald jemand danach handeln muss.
   */
  durationMs?: number;
  dismissLabel?: string;
  className?: string;
}

/**
 * Eine kurze Bestätigung — „Gespeichert.", „Freigabe erteilt."
 *
 * Nur für Dinge, die **erledigt** sind. Nicht für Fehler (die gehören an die
 * Stelle, an der sie entstanden, als `Alert`), nicht für etwas, das noch
 * läuft, und **niemals** für die Zusage aus ADR-0027 §6 („Deine Löschung ist
 * angenommen und läuft") — die muss auf der Seite stehen bleiben, wo die
 * Person sie wiederfindet.
 */
export function Toast({
  message,
  onDismiss,
  durationMs,
  dismissLabel = "Ausblenden",
  className,
}: ToastProps) {
  useEffect(() => {
    if (durationMs === undefined) return;
    const timer = setTimeout(onDismiss, durationMs);
    return () => clearTimeout(timer);
  }, [durationMs, onDismiss]);

  return (
    <div className={["wt-toast", className].filter(Boolean).join(" ")}>
      {/* Die Live-Region trägt den Text, damit er auch angesagt wird und nicht
          nur erscheint. */}
      <p className="wt-toast__message" role="status" aria-live="polite">
        {message}
      </p>
      {/* Von Hand ausblenden geht immer — auch wenn eine Zeitgrenze gesetzt
          ist. Wer gelesen hat, muss nicht warten. */}
      <button type="button" className="wt-button wt-button--quiet" onClick={onDismiss}>
        {dismissLabel}
      </button>
    </div>
  );
}
```

Create `packages/ui/src/styles/toast.css`:

```css
.wt-toast {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--wt-space-4);
  border: 1px solid var(--wt-border);
  border-radius: var(--wt-radius-sm);
  padding: var(--wt-space-3) var(--wt-space-4);
  background: var(--wt-surface-raised);
  box-shadow: var(--wt-shadow);
}

.wt-toast__message {
  margin: 0;
  font-size: var(--wt-text-base);
  line-height: var(--wt-leading-tight);
}
```

Add the `@import` line and export `Toast`, `ToastProps`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --filter @workertransfer/ui run test && pnpm --filter @workertransfer/ui run check`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/ui/src/components/toast.tsx packages/ui/src/components/toast.test.tsx \
        packages/ui/src/styles/toast.css packages/ui/src/styles.css packages/ui/src/index.ts
git commit -m "feat(ui): Toast — bleibt stehen, wenn niemand ihn wegschickt

Kein Verbraucher heute. Der Standard ist mit Absicht KEIN automatisches
Verschwinden: eine Meldung, die von selbst geht, ist schlechter als eine, die
bleibt, sobald jemand danach handeln muss. durationMs ist eine Zusage, die man
gibt, nicht eine, die man erbt.

Im Docstring steht, wofür er NICHT ist: nicht für Fehler (die gehören als
Alert an die Stelle, an der sie entstanden) und niemals für die Zusage aus
ADR-0027 §6, die auf der Seite stehen bleiben muss.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 16: Das Screenshot-Werkzeug

Die Palettenvereinigung aus Task 1 ist sichtbar. Ohne Bilder ist die Aussage
„drei Farben haben sich geändert, sonst nichts" unbelegt.

**Files:**
- Create: `apps/web/playwright.shots.config.ts`
- Create: `apps/web/e2e-shots/oberflaeche.shots.ts`
- Modify: `apps/web/package.json` (Skript `shots`), `.gitignore`

**Interfaces:**
- Consumes: `WEB_URL` und `missingService` aus `apps/web/e2e/stack.ts`.
- Produces: das Skript `pnpm --filter @workertransfer/web run shots`, Ausgabe
  in `.screenshots/<SHOT_TAG>/<name>.png`.

**Zwei Entscheidungen, die im Dateikopf stehen müssen:**
1. **Nicht in `apps/web/e2e/`.** `scripts/validate.sh` zählt „Reisen" aus dem
   Playwright-Protokoll (`e2e_ran=$(count_from "$playwright_log" '[0-9]+ passed')`).
   Ein Screenshot-Spec dort blähte die Zahl auf und entwertete genau den
   Zähler, der eingebaut wurde, weil sich einmal alle 16 Reisen übersprungen
   haben und der Bericht „Alles grün" sagte. Die Datei heißt darum
   `*.shots.ts` und nicht `*.spec.ts` — Playwrights Standard-`testMatch`
   greift sie nicht.
2. **Nur öffentlich erreichbare Seiten.** `/`, `/login`, `/register`, `/jobs`
   brauchen keine Anmeldung und zeigen zusammen Hero, Auth-Panel, Karten,
   Knöpfe, Felder und einen `<select>`. Ein angemeldeter Blick käme ohne
   Gewinn mit einer Registrierung samt Mailabruf je Lauf.

- [ ] **Step 1: Die Konfiguration schreiben**

Create `apps/web/playwright.shots.config.ts`:

```ts
import { defineConfig, devices } from "@playwright/test";

/**
 * Getrennte Konfiguration für Screenshot-Aufnahmen — bewusst NICHT Teil von
 * `playwright.config.ts`.
 *
 * Der Grund ist der Zähler: `scripts/validate.sh` liest die Zahl bestandener
 * „Reisen" aus dem Playwright-Protokoll, weil sich einmal alle 16 Reisen
 * stillschweigend übersprungen haben und der Bericht trotzdem „Alles grün"
 * sagte. Aufnahmen in derselben Suite würden diese Zahl aufblähen und den
 * Wächter entwerten.
 *
 * Es gibt keine eingecheckten Referenzbilder und kein CI-Gate: Schriftglättung
 * und Plattform (darwin lokal, linux in der CI) erzeugen Fehlalarme, und die
 * CI fährt den Stapel gar nicht. Verglichen wird mit dem Auge, im PR.
 */
export default defineConfig({
  testDir: "./e2e-shots",
  // Die Aufnahmedateien heißen *.shots.ts, damit die Standard-testMatch der
  // Hauptkonfiguration sie nicht einsammelt.
  testMatch: /.*\.shots\.ts/,
  workers: 1,
  fullyParallel: false,
  reporter: [["list"]],
  timeout: 60_000,
  use: {
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:5173",
    // Feste Bildgröße: ein Vergleich zweier Bilder unterschiedlicher Breite
    // zeigt Umbrüche, nicht Farben.
    viewport: { width: 1280, height: 900 },
    ...devices["Desktop Chrome"],
  },
});
```

- [ ] **Step 2: Die Aufnahmedatei schreiben**

Create `apps/web/e2e-shots/oberflaeche.shots.ts`:

```ts
import { expect, test } from "@playwright/test";

import { missingService } from "../e2e/stack";

/**
 * Nimmt die öffentlich erreichbaren Seiten auf — einmal vor einer Umstellung,
 * einmal danach.
 *
 *   docker compose up -d
 *   SHOT_TAG=vorher  pnpm --filter @workertransfer/web run shots
 *   …umstellen…
 *   SHOT_TAG=nachher pnpm --filter @workertransfer/web run shots
 *
 * Die Bilder gehören in den PR-Text, nicht ins Repository.
 */
const TAG = process.env.SHOT_TAG ?? "aktuell";

const SEITEN: { name: string; pfad: string }[] = [
  { name: "startseite", pfad: "/" },
  { name: "anmelden", pfad: "/login" },
  { name: "registrieren", pfad: "/register" },
  // Öffentlich, und die einzige der vier mit einem <select> und Karten.
  { name: "stellen", pfad: "/jobs" },
];

test.describe("Aufnahmen der Oberfläche", () => {
  test.beforeAll(async () => {
    const missing = await missingService();
    test.skip(missing !== null, `Stapel steht nicht: ${missing}`);
  });

  for (const seite of SEITEN) {
    test(`${seite.name} (${TAG})`, async ({ page }) => {
      // Der Direktlink, nicht ein Klick: nur das Eintippen der Adresse geht
      // wirklich durchs Gateway (siehe docker/traefik/dynamic.yml,
      // Sec-Fetch-Dest). Ein Klick schaltet im Browser um und beweist nichts.
      await page.goto(seite.pfad);
      // Eine <h1> ist der Beleg, dass die Seite und nicht rohes JSON kam.
      await expect(page.locator("h1").first()).toBeVisible();
      await page.screenshot({
        path: `.screenshots/${TAG}/${seite.name}.png`,
        fullPage: true,
      });
    });
  }
});
```

- [ ] **Step 3: Skript und `.gitignore` ergänzen**

Add to `apps/web/package.json` scripts, after `"e2e:install"`:
```json
"shots": "playwright test --config playwright.shots.config.ts"
```

Add to `.gitignore`:
```
# Screenshot-Aufnahmen für den Vorher/Nachher-Vergleich im PR. Bilder, keine
# Quellen — und plattformabhängig, deshalb bewusst keine Referenzbilder im
# Repository.
.screenshots/
```

- [ ] **Step 4: Prüfen, dass die Hauptsuite die Aufnahmen NICHT einsammelt**

Das ist der Punkt, an dem die ganze Trennung hängt.

```bash
cd /Users/davidozturk/Projects/workertransfer/apps/web
pnpm exec playwright test --list | grep -c 'shots' || echo "0 — richtig"
pnpm exec playwright test --config playwright.shots.config.ts --list
```
Expected: die erste Zeile sagt `0 — richtig`; die zweite listet vier Aufnahmen.

- [ ] **Step 5: Aufnahmen machen (braucht den Stapel)**

Erst hier läuft Docker — und dann laufen **keine** Unit-Tests nebenher
(Falle 2).

```bash
cd /Users/davidozturk/Projects/workertransfer
docker compose up -d
for port in 8001 8006; do curl -sf "localhost:$port/health/live" >/dev/null && echo "$port ok" || echo "$port TOT"; done
SHOT_TAG=nachher pnpm --filter @workertransfer/web run shots
ls -1 .screenshots/nachher/
```
Expected: vier PNG-Dateien. Meldet der Lauf `skipped`, steht der Stapel nicht —
dann ist auch das Bild nichts wert, und `missingService()` sagt welcher Dienst
fehlt.

Für den Vorher-Stand: `git stash`, `SHOT_TAG=vorher … run shots`,
`git stash pop`. Die Vorher-Bilder zeigen `#1f6f5c`, die Nachher-Bilder
`#1b6a47`.

- [ ] **Step 6: Stapel herunterfahren, dann committen**

```bash
docker compose stop
git add apps/web/playwright.shots.config.ts apps/web/e2e-shots/ apps/web/package.json .gitignore
git commit -m "test(web): Screenshot-Aufnahmen — getrennt, damit der Reisezähler stimmt

Die Palettenvereinigung ist sichtbar; ohne Bilder ist \"drei Farben haben sich
geändert, sonst nichts\" unbelegt.

Eigene Konfiguration und *.shots.ts statt *.spec.ts, damit die Hauptsuite sie
nicht einsammelt: scripts/validate.sh liest die Zahl bestandener Reisen aus dem
Playwright-Protokoll, weil sich einmal alle 16 Reisen stillschweigend
übersprungen haben und der Bericht trotzdem \"Alles grün\" sagte. Aufnahmen in
derselben Suite hätten diesen Wächter entwertet.

Keine Referenzbilder im Repository und kein CI-Gate: Schriftglättung und
Plattform erzeugen Fehlalarme, und die CI fährt den Stapel nicht.

Aufgerufen wird per Direktlink, nicht per Klick — nur das Eintippen der
Adresse geht wirklich durchs Gateway.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 17: ADR-0029, ROADMAP, `docs/frontend.md` und das Gesamtgate

**Files:**
- Create: `docs/adr/0029-designsystem-traegt-die-oberflaeche.md`
- Modify: `docs/ROADMAP.md` (Querschnitt-Abschnitt), `docs/frontend.md` (Inventar)

**Interfaces:**
- Consumes: alle 15 Bauteile aus den Aufgaben 2–15.

- [ ] **Step 1: ADR-0029 schreiben**

Create `docs/adr/0029-designsystem-traegt-die-oberflaeche.md` mit dem Aufbau
der bestehenden ADRs (Titel, Status, Kontext, Entscheidung, Folgen,
Alternativen). Inhalt, der darin **festgehalten** werden muss:

1. Eigene Primitives, keine Bibliothek — bestätigt, nicht neu entschieden.
2. **Natives Element vor selbstgebautem**, mit den drei Belegen: `<select>`
   (APG verlangt `aria-activedescendant`, kein Bestandsfall braucht
   Autocomplete), `<dialog>` (der Browser liefert vier der fünf Forderungen),
   `<fieldset>`/`<input type=radio>` (der gemeinsame `name` verdrahtet die
   Pfeiltasten).
3. **Eine Palette, ein Definitionsort, keine Fallbacks** — mit der
   Vorgeschichte der zwei Grüntöne und den zwei Wächtern.
4. **Kein Bauteil, das einen Menschen als Zahl darstellt** (verweist auf
   ADR-0022), und die Grenze am `Badge`.
5. **Beweisgrenzen werden benannt, nicht überspielt**: die Dialog-Fokusfalle
   ist in jsdom nicht prüfbar; der Shim ahmt sie bewusst nicht nach.
6. **Drei Bauteile ohne Verbraucher** (`Skeleton`, `Dialog`, `Toast`) und die
   Zusage, dass der letzte E3-PR über sie entscheidet.
7. Kein Dark-Mode, kein i18n.

- [ ] **Step 2: ROADMAP-Eintrag schreiben**

Add a `### Querschnitt — Das Design-System ✅ (09.08.2026)` section to
`docs/ROADMAP.md`, im Stil der vorhandenen Querschnitte. **Kein
`### Phase N`-Format**: `tests/test_roadmap_status_is_consistent.py` vergleicht
Tabellenzeilen mit `### Phase N — Status: <Symbol>`-Überschriften, und ein
Querschnitt gehört nicht in die Tabelle.

- [ ] **Step 3: Prüfen, dass der Wächtertest nicht anspringt**

Run: `uv run pytest tests/test_roadmap_status_is_consistent.py -v`
Expected: PASS. Läuft er rot, hat der neue Abschnitt versehentlich das
`### Phase N`-Format erwischt.

- [ ] **Step 4: `docs/frontend.md` richtigstellen**

Die Datei ist grob veraltet: Stand „2026-07-31", „**Two routes**", „`Button`
and `Card`", „Node 24". Zu ersetzen sind der Abschnitt „What exists today"
und die Angabe der CI-Node-Version (CI fährt Node **25**).

Neu hinzu: ein Inventar-Abschnitt mit den zwanzig Bauteilen, je einer Zeile,
und der Regel, wo CSS hingehört (`packages/ui/src/styles/<bauteil>.css`; in
`apps/web/src/styles.css` nur, was wirklich das Layout genau einer Seite ist).

**Das Routenmuster kommt NICHT hierher** — das ist E2, und es ohne eine
umgestellte Route hinzuschreiben wäre eine Behauptung ohne Beleg.

- [ ] **Step 5: Das volle Gate**

Kein Docker-Stapel darf laufen (Falle 2).

```bash
cd /Users/davidozturk/Projects/workertransfer
docker compose ps --status running   # muss leer sein
make check-web
pnpm build
```
Expected: alles grün.

- [ ] **Step 6: Die Zahlen messen, die in den PR gehören**

```bash
# Bauteile: müssen 20 sein (5 alt + 15 neu)
ls -1 packages/ui/src/components/*.tsx | grep -v '\.test\.' | wc -l
# Testfälle im Paket: Ausgangswert war 20
grep -rhc 'it(' packages/ui/src/components/*.test.tsx packages/ui/src/styles.test.ts | awk '{s+=$1} END {print s}'
# apps/web muss UNVERÄNDERT bei 386 stehen (+3 aus styles.test.ts = 389)
grep -rhc 'it(\|test(' $(find apps/web/src -name '*.test.ts' -o -name '*.test.tsx') | awk '{s+=$1} END {print s}'
# Tokens: müssen >= 25 sein
grep -cE '^\s+--wt-[a-z0-9-]+\s*:' packages/ui/src/styles/tokens.css
# styles.css in apps/web: unverändert 885 Zeilen (E1 stellt keine Route um)
wc -l apps/web/src/styles.css
```

- [ ] **Step 7: Commit**

```bash
git add docs/adr/0029-designsystem-traegt-die-oberflaeche.md docs/ROADMAP.md docs/frontend.md
git commit -m "docs: ADR-0029 und der Stand der Oberfläche

Hält fest, was in E1 entschieden wurde: natives Element vor selbstgebautem
(mit den drei Belegen), eine Palette an einem Definitionsort ohne Fallbacks,
kein Bauteil, das einen Menschen als Zahl darstellt, und die Zusage, dass
Beweisgrenzen benannt werden statt überspielt — die Dialog-Fokusfalle ist in
jsdom nicht prüfbar, und der Shim ahmt sie bewusst nicht nach.

docs/frontend.md war grob veraltet: Stand 2026-07-31, \"Two routes\", \"Button
and Card\", Node 24 (die CI fährt 25). Das Routenmuster kommt bewusst NICHT
hierher — das ist E2, und es ohne eine umgestellte Route hinzuschreiben wäre
eine Behauptung ohne Beleg.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Anhalten**

E1 ist fertig. **Nicht mit E2 beginnen.** Der PR-Text enthält:

- die gemessenen Zahlen aus Step 6;
- die **drei beabsichtigten sichtbaren Änderungen** (`--wt-accent`
  `#1f6f5c`→`#1b6a47`, `--wt-border` `#d6d3cd`→`#dbe4db`, `--wt-surface-muted`
  `#f4f1ec`→abgeleitet) mit den Vorher/Nachher-Bildern aus Task 16;
- die Beweisgrenze beim `Dialog`, wörtlich;
- die drei Bauteile ohne Verbraucher und wann über sie entschieden wird.

## Self-Review

**Spec-Deckung.** Abschnitt E1.1 (Tokens, Wächter, kein Dark-Mode) → Task 1.
E1.2 Gruppe A → Tasks 2–6; Gruppe B → Tasks 7–9; Gruppe C → Tasks 10–13;
Gruppe D → Tasks 14–15. E1.3 (Beweis, ADR-0029) → Task 17. Screenshot-Beweis
→ Task 16. „Nicht gebaut" (`Table`, `Tabs`, `Pagination`, `Progress`,
`Tooltip`, `Avatar`) → keine Aufgabe, absichtlich, begründet in der Spec und
in ADR-0029 Punkt 6. Die Abschnitte E2, E2.5, E2.6 und E3 sind **nicht** Teil
dieses Plans und bekommen eigene, jeweils nach ihrem Befund-Gate.

**Zwei Lücken gefunden und geschlossen:** die Spec zählt `VisuallyHidden` als
eigenes Bauteil, mein erster Zuschnitt hatte es nur als CSS-Datei in Task 5 —
jetzt ist es eine Komponente in Task 12. Und die Spec verlangt eine
Playwright-Prüfung der Dialog-Fokusfalle; die ist in E1 **unmöglich**, weil
kein Verbraucher existiert, den man öffnen könnte. Statt sie stillschweigend
weglassen, steht die Grenze jetzt in Task 14, im ADR und im PR-Text.

**Typ-Konsistenz.** Geprüft: `Loading.label` ist überall `string` (nicht
`ReactNode`), weil `useAnnounce` denselben Text weitergeben können muss.
`Alert.variant` heißt in Test, Implementierung und CSS gleich
(`error`/`notice`). `RadioGroup.onChange` bekommt in Test und Implementierung
den **Wert**, nicht das Ereignis. `Badge` gibt `null` bei `count <= 0` — Test
und Implementierung stimmen. `Dialog.onClose` wird sowohl vom Knopf als auch
vom `close`-Ereignis erreicht; der Knopf ruft `ref.current?.close()` und nicht
`onClose()` direkt, damit es genau **einen** Weg gibt.

**Platzhalter.** Keine. Task 17 Step 1 und 2 beschreiben Dokumenttexte durch
ihre sieben bzw. einen Pflichtinhalt statt sie auszuschreiben — das ist Prosa
für Menschen, kein Code, und die Punkte sind einzeln nachprüfbar.
