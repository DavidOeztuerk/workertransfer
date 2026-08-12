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

## Wie eine Route ab jetzt aussieht

Festgelegt an `/login`, `/register` und `/` (Schnitt E2). Wer eine Route
umstellt, schreibt sie nach diesem Muster ab.

### Die vier Zustände, in dieser Reihenfolge

```tsx
const query = useQuery({ queryKey: […], queryFn: … });

if (query.isPending) return <Loading label="Portfolio wird geladen…" />;
if (query.isError)   return <Alert>{fehlertext}</Alert>;
if (leer)            return <Empty title="Noch keine Arbeiten." />;
return <Inhalt … />;
```

**Fehler vor Leer vor Inhalt** — verbindlich. Eine leere Liste, weil der Abruf
scheiterte, ist kein Leerzustand, und „hier ist noch nichts" wäre dann die
beruhigendste falsche Antwort, die es gibt.

- Das `label` von `Loading` ist **routenspezifisch** und kommt vom Aufrufer:
  „was lädt", nicht „dass etwas lädt".
- Der Fehlertext kommt **vom Server** und wird nie erfunden. Wo ein Client eine
  diskriminierte Union zurückgibt (`LoginResult`), wird sie ausgewertet statt
  `try/catch` um alles zu legen.
- `Alert` ohne Variante unterbricht (`role="alert"`); Bestätigungen bekommen
  `variant="notice"` (`role="status"`).

### Mutationen

```tsx
<Button type="submit" disabled={busy}>{busy ? "Anmeldung läuft…" : "Anmelden"}</Button>
{fehler !== null ? <Alert>{fehler}</Alert> : null}
```

Jede Mutation braucht **drei** sichtbare Zustände: läuft, ging schief, ging
durch. Fehlt der mittlere, entsteht ein Knopf, der bei einem Netzfehler nichts
tut — genau das war der Fall bei „E-Mail erneut senden". Für Erfolge, die keine
Navigation auslösen, kommt `useAnnounce()` dazu, sonst erfährt ein Screenreader
nichts.

### Navigieren

`Button` mit `href` ist ein `<a>`, ohne `href` ein `<button>`. Was navigiert,
muss ein Link sein — ein `<button onClick={navigate}>` nimmt Mittelklick, neuen
Tab, Adresse-kopieren und die Vorschau in der Statusleiste.

### Wo CSS hingehört

| Ort | Was |
|---|---|
| `packages/ui/src/styles/<bauteil>.css` | portable Primitives — Knopf, Feld, Karte, Zeile |
| `apps/web/src/routes/<route>.css`, neben der Route | Layout **genau einer** Seite oder einer Produkt-Hülle (`home.css`, `auth-layout.css`) |
| `apps/web/src/styles.css` | nur noch, was mehrere noch nicht umgestellte Routen teilen — schrumpft mit jedem Schnitt |

**Nicht tokenisieren, was kein Systemwert ist.** `1.6` ist nicht
`--wt-leading-normal` (1.55), `0.9rem` keine Systemschriftgröße. In E1 hat genau
dieses Zusammenziehen jedes Formular um einen Pixel verschoben; gefunden hat es
der Screenshot-Vergleich, kein Test.

**Eine geteilte Klasse zieht erst um, wenn ihr letzter Aufrufer weg ist.**
`.auth__alert` liegt weiter in `styles.css`, weil acht nicht umgestellte Routen
sie benutzen — der Name lügt, die Abhängigkeit nicht.

### Was eine Umstellung nicht darf

- Texte ändern, wo es nicht ausdrücklich gewollt ist: die Tests prüfen die
  deutschen Literale direkt.
- Eine Zusage verlieren. Beispiele aus diesen drei Routen: „Danach geht es
  zurück zu: *Stelle*" (`ZurueckHinweis`), das Ziel `/jobs?stelle=<id>` nach dem
  Anmelden, und für eine bekannte Adresse **dieselbe** Antwort wie für eine neue.
- Eine sichtbare Änderung unangekündigt lassen. Jeder PR trägt zwei Listen: was
  sich absichtlich geändert hat, und welche Testzahl sich deswegen bewegt hat.

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
