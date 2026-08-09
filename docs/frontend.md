# Frontend architecture

The frontend is a pnpm workspace driven by Turborepo:

```text
apps/web              Vite + React application shell
packages/ui           accessible, product-owned UI primitives and design tokens
```

The application owns feature modules, pages, route composition, API clients, and
server-state hooks. `packages/ui` contains only portable design-system primitives;
it must not import feature logic, application state, or backend clients.

## What exists today (10.08.2026)

- **26 Routen** unter `apps/web/src/routes/` (5.224 Zeilen ohne Tests), von `/`
  und `/login` bis `/konto-loeschen` und `/company/team`. Die vollständige Liste
  steht in CLAUDE.md.
- **Session state**: `src/auth/session.ts` (`useSession` / `useLogout`) ist die
  einzige Quelle für „bin ich angemeldet", getragen von TanStack Query über
  `GET /me`. `src/auth/client.ts` ist der cookie-basierte Client;
  `LoginResult` ist eine diskriminierte Union und wirft bei falschen
  Zugangsdaten nicht.
- **`packages/ui`**: **20 Bauteile**, handgeschriebenes CSS mit `--wt-*` Custom
  Properties. **Kein** Tailwind, Shadcn, Radix, Storybook, ESLint oder Prettier.
  Das Paket exportiert rohes TypeScript (kein Build-Schritt).
- **Kein i18n.** Deutsch ist in JSX hartkodiert und an einer Stelle im
  API-Client (`"Anmeldung fehlgeschlagen"`); die Tests prüfen die deutschen
  Literale direkt. Jede Textänderung kostet einen Test.
- **Formulare** benutzen einfaches `useState` — kein React Hook Form, kein Zod.
- **Passung** (`src/jobs/match.ts`) wird im Browser gerechnet und existiert
  nirgends sonst: eine Liste mit Haken, niemals eine Zahl (ADR-0022).

## Das Design-System

Regeln, die in ADR-0029 begründet sind:

- **Natives Element vor selbstgebautem.** `Select` ist ein `<select>`, `Dialog`
  ein `<dialog>` mit `showModal()`, `RadioGroup` sind Radios mit gemeinsamem
  `name`. Wer ein Verhalten nicht sauber selbst hinbekommt, nimmt das native
  Element — ein hübscher Dialog, den man mit der Tastatur nicht verlassen kann,
  ist schlechter als ein hässlicher.
- **Tokens nur in `packages/ui/src/styles/tokens.css`.** Ein
  `var(--wt-x, fallback)` ist **verboten**, auch bei definiertem Token: der
  Fallback war der Weg, auf dem einmal eine zweite Palette entstand. Zwei
  Wächtertests halten das fest.
- **CSS gehört nach `packages/ui/src/styles/<bauteil>.css`**, eine Datei je
  Bauteil, eingebunden über die reine `@import`-Liste in
  `packages/ui/src/styles.css`. In `apps/web/src/styles.css` gehört nur, was
  wirklich das Layout **genau einer** Seite ist.
- **Kein Bauteil, das einen Menschen als Zahl darstellt** (ADR-0022): kein
  Score, kein Prozent, kein Ranking, kein Fortschrittsbalken über Personen.
- **Kein Dark-Mode**, solange niemand ihn anfordert.

### Inventar

| Bauteil | Wofür |
|---|---|
| `Button` | `primary` / `secondary` / `quiet` |
| `Card` | abgesetzte Fläche mit Rahmen und Schatten |
| `Field` | einzeiliges Feld mit Label, Hinweis, Fehler |
| `TextArea` | wie `Field`, mehrzeilig |
| `Select` | natives `<select>` in der `Field`-Hülle |
| `Checkbox` | Kästchen — gilt **mit dem Absenden** |
| `Switch` | `button[role="switch"]` — wirkt **sofort** |
| `Fieldset` | benannte Feldgruppe (`<fieldset>` + `<legend>`) |
| `RadioGroup` | Auswahl aus wenigen benannten Zuständen |
| `Page` | Seitengerüst: `<main>`, eine `<h1>`, Vorspann, Nachsatz, Rückweg |
| `RowList` / `Row` | eine Zeile je Vorgang: Titel, Meta, Aktionen |
| `DescriptionList` | Begriff/Wert-Paare als `<dl>` |
| `Loading` | Ladeanzeige — das Label sagt, **was** lädt |
| `Skeleton` | Platzhalter, `aria-hidden`, immer neben ein `Loading` |
| `Alert` | `error` unterbricht (`role="alert"`), `notice` wartet (`role="status"`) |
| `Empty` | „hier ist noch nichts" — bewusst ohne `role` |
| `LiveRegion` / `useAnnounce` | sagt Ergebnisse an, die man sonst nur sieht |
| `Toast` | kurze Bestätigung; verschwindet **nicht** von selbst |
| `Badge` | Zähler für **Vorgänge**, niemals für Personen |
| `VisuallyHidden` | gelesen, aber nicht gesehen — das Gegenteil von `aria-hidden` |

Der Unterschied zwischen `Checkbox` und `Switch` ist keine Kosmetik: eine
Checkbox verspricht „gilt, wenn du absendest", ein Switch wirkt sofort. Bei
einer Einwilligung entscheidet dieses Versprechen, was die Person glaubt getan
zu haben.

`Skeleton`, `Dialog` und `Toast` haben heute **keinen Aufrufer**. Der letzte
E3-PR entscheidet je Bauteil: Verbraucher oder Löschung (ADR-0029 §6).

### Tests im Design-System

Jedes Bauteil hat einen Test, der **Verhalten** prüft — Rolle, zugänglicher
Name, zugängliche Beschreibung, Tastatur, Fokus — und **nicht** Klassennamen.
`screen.getByRole(...)` statt `container.querySelector(".wt-…")`; wo eine
Ausnahme nötig ist, steht der Grund als Kommentar im Test.

**Eine Grenze, die man kennen muss:** jsdom implementiert
`HTMLDialogElement.showModal()` nicht (Issue #3294). Der Ersatz in
`packages/ui/src/test/setup.ts` setzt nur `open` und löst `close` aus und ahmt
die Fokusfalle **absichtlich nicht** nach — sonst prüften die Tests unseren
eigenen Ersatz. Fokusfalle und Esc gehören deshalb in eine Browser-Prüfung.

## Commands

```bash
pnpm install
pnpm check      # tsc --noEmit across the workspace
pnpm test       # Vitest (apps/web und packages/ui)
pnpm build
pnpm dev
make check-web  # pnpm check + pnpm test, die Gate-Schritte 5 und 6
```

Alle vier laufen im CI-Job `frontend-quality` auf **Node 25**. Die Node-Version
in `ci.yml` (zwei Stellen) muss dem Major im Dockerfile entsprechen, sonst
prüft die CI etwas anderes als ausgeliefert wird.

Playwright-E2E liegt in `apps/web/e2e/` und läuft gegen den **echten**
`docker compose`-Stapel; ohne Stapel überspringen sich die Reisen selbst.

Screenshot-Aufnahmen für den Vorher/Nachher-Vergleich in einem PR:

```bash
docker compose up -d
SHOT_TAG=nachher pnpm --filter @workertransfer/web run shots
```

Sie liegen in `apps/web/e2e-shots/` mit **eigener** Konfiguration und heißen
`*.shots.ts`, damit `playwright test` sie nicht einsammelt: `scripts/validate.sh`
zählt bestandene Reisen aus dem Protokoll, und Aufnahmen in derselben Suite
würden diesen Zähler entwerten. Ausgabe nach `.screenshots/<tag>/`, nicht
verfolgt.

`VITE_API_BASE_URL` ist als Turborepo-Build-Input deklariert. Geheimnisse tragen
nie ein `VITE_`-Präfix. `apps/web/src/env.ts` löst die Dienstadressen in drei
Schritten auf (`window.__WT_CONFIG__` → `VITE_*` → Port-Rückfall), weil ein
gebautes Artefakt keine eingebackenen URLs haben darf (ADR-0028).
