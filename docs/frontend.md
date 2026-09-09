# Frontend architecture

Alles Frontend liegt in **`web/`** — ein Verzeichnis, ein `package.json`, ein
Lockfile. Es gibt keinen pnpm-Workspace, kein Turborepo, kein `packages/ui` und
kein `package.json` in der Wurzel mehr. `pnpm -r` und `--filter` scheitern hier
mit `ERR_PNPM_NO_PKG_MANIFEST`; jeder Befehl läuft aus `web/`.

```text
web/src/
  main.tsx  AppRoot.tsx  env.ts
  core/       api/ (ein fetch-Client)  router/ (createBrowserRouter)
              store/ (Redux Toolkit)   config/
  features/   public/  auth/  person/  work/  company/
              je Feature: api/  components/  pages/  lib/  routes.tsx  test/
  shared/     components/{layout,ui,routing}  hooks/  pages/
  styles/     tokens/{colors,spacing,typography}.ts  theme.ts
  test/
web/e2e/      die Playwright-Reisen
```

**Feature-driven**: was zu einer Fachlichkeit gehört, liegt beieinander — der
API-Client neben den Seiten, die ihn rufen, und den Bauteilen, die nur dort
vorkommen. Was zwei Features teilen, zieht nach `shared/`, und erst dann. Ein
Feature importiert **nie** aus einem anderen Feature; braucht es etwas von dort,
gehört das nach `shared/` oder `core/`.

## Der Aufbau

**MUI 9 mit Emotion** trägt die Oberfläche. Es gibt kein handgeschriebenes CSS
mehr und keine `--wt-*`-Eigenschaften: Farben, Abstände und Schrift stehen in
`src/styles/tokens/` als einzige Quelle, `src/styles/theme.ts` baut daraus das
MUI-Theme für hell und dunkel. **Ein Farbliteral in einer Komponente ist ein
Mangel, keine Abkürzung** — es entzieht sich dem Themenwechsel und ist beim
nächsten Farbwechsel die eine Stelle, die niemand findet.

Der Modus kommt aus `useThemeMode()` — bewusst nicht `useTheme`, das gehört MUI
und läse sich an der Aufrufstelle wie dessen Hook.

**Die Palette lässt Grün bewusst aus.** Auf einer Plattform, die über
Einwilligungen entscheidet, *ist* Grün ein Signal („erteilt") und darf nicht
zugleich die Hausfarbe sein — die Vermischung nimmt dem Signal seine Bedeutung.
Indigo trägt, Bernstein akzentuiert sparsam, und Grün/Rot/Bernstein bleiben frei
für erteilt, widerrufen, läuft.

**Redux Toolkit** hält den Zustand. Jeder Thunk geht über `createAppThunk`
(`core/store/thunkHelpers.ts`), damit `rejectValue` überall dieselbe Gestalt hat:
`ApiError` mit `status`, `title`, `detail` und `correlationId`. Ohne diese eine
Gestalt prüft jede Komponente einen anderen Fehler, und einer davon wird
vergessen.

**`react-router-dom` mit `createBrowserRouter`.** Jedes Feature bringt seine
Routen in `features/<name>/routes.tsx` selbst mit; `core/router/` fügt sie
zusammen. Seiten werden über `lazyRoute()` nachgeladen, mit einem `Suspense`
**je Route** — so bleibt die Kopfzeile stehen, während der Inhalt kommt.

## Wie eine Route aussieht

### Die vier Zustände, in dieser Reihenfolge

```tsx
const { data, status, error } = useAppSelector(auswahl);

if (status === "pending") return <LoadingBlock label="Portfolio wird geladen…" />;
if (error !== null)       return <ErrorBlock error={error} />;
if (data.length === 0)    return <EmptyBlock title="Noch keine Arbeiten." />;
return <Inhalt … />;
```

**Fehler vor Leer vor Inhalt** — verbindlich. Eine leere Liste, weil der Abruf
scheiterte, ist kein Leerzustand, und „hier ist noch nichts" wäre dann die
beruhigendste falsche Antwort, die es gibt.

Die drei Bauteile stehen in `shared/components/ui/StateBlock.tsx`:

- `LoadingBlock` trägt ein **routenspezifisches** `label` vom Aufrufer: *was*
  lädt, nicht *dass* etwas lädt. Es hat `role="status"`, sonst erfährt ein
  Screenreader nichts.
- `ErrorBlock` zeigt den Text **vom Server** und erfindet nie einen — und
  darunter klein die `correlationId`. Sie ist der einzige Faden, an dem sich eine
  Beschwerde durch alle Dienste zurückverfolgen lässt; wer sie weglässt, zwingt
  die Person, den Zeitpunkt zu schätzen.
- `EmptyBlock` darf **„leer" und „nicht freigegeben" nie verwischen**. Wer nichts
  sieht, weil niemand freigegeben hat, muss einen anderen Satz lesen als jemand,
  der wirklich nichts angelegt hat.

### Mutationen

Jede Mutation braucht **drei** sichtbare Zustände: läuft, ging schief, ging
durch. Fehlt der mittlere, entsteht ein Knopf, der bei einem Netzfehler nichts
tut — genau das war der Fall bei „E-Mail erneut senden". Für Erfolge, die keine
Navigation auslösen, kommt eine Ansage dazu.

### Navigieren

Was navigiert, muss ein Link sein. Ein `<button onClick={navigate}>` nimmt
Mittelklick, neuen Tab, Adresse-kopieren und die Vorschau in der Statusleiste.
In MUI heißt das `component={RouterLink}` beziehungsweise `href`.

### Der Draht ist snake_case

Die API-Typen bilden ihn ab, statt ihn zu übersetzen. Eine camelCase-Fassung
dazwischen war schon einmal ein schwerer Fehler: die Felder kamen beim Server
nicht an, und niemand sah es, weil das Formular danach trotzdem grün meldete.
Einwortfelder verdecken den Unterschied — nur zusammengesetzte Namen gehen
auseinander, und die fallen erst in einer echten Anfrage auf.

### Was eine Änderung nicht darf

- **Texte ändern, wo es nicht ausdrücklich gewollt ist**: die Oberfläche ist
  deutsch und **hartcodiert**, es gibt keine i18n-Schicht, und die Tests prüfen
  die deutschen Literale direkt.
- **Eine Zusage verlieren.** Beispiele: „Danach geht es zurück zu: *Stelle*",
  das Ziel `/jobs/<id>/apply` nach dem Anmelden, und für eine bekannte Adresse
  **dieselbe** Antwort wie für eine neue.
- **Einen Einwilligungsschalter zur Ankreuzbox machen.** `ConsentSwitch` ist ein
  `button[role="switch"]`: eine Ankreuzbox verspricht, die Änderung gelte beim
  Absenden, und bei einer Einwilligung ist dieser Unterschied nicht kosmetisch.

## Tests

Geprüft wird **Verhalten** — Rolle, zugänglicher Name, Tastatur, Fokus — und
**nicht** Klassennamen: `screen.getByRole(...)` statt `container.querySelector`.
Wo eine Ausnahme nötig ist, steht der Grund als Kommentar im Test.

Die Playwright-Reisen in `web/e2e/` laufen gegen den **echten** Compose-Stapel;
es gibt bewusst keinen `webServer` in `playwright.config.ts`, weil ein halb
hochgefahrenes Umfeld genau die Integration wegabstrahierte, für die es sie
gibt. **Ohne Stapel überspringen sie sich selbst**, und ein übersprungener Test
sieht aus wie ein bestandener — deshalb liest der CI-Job `e2e` die Zahl der
gefahrenen Reisen und geht rot, wenn es zu wenige waren.

## Commands

```bash
cd web && pnpm check     # tsc --noEmit
cd web && pnpm test      # Vitest
cd web && pnpm build     # das Bündel — gehört dazu, siehe unten
cd web && pnpm e2e       # die Reisen (braucht `make up`)
```

`pnpm build` ist kein Beiwerk: `tsc` und Vitest laufen beide **nicht** über den
Bauweg, und ein Fehler, der erst beim Bündeln auftritt, fällt sonst erst im Bild
auf — und das baut hier niemand nebenbei.

## Laufzeitkonfiguration, nicht Bauzeit

`vite build` backt `import.meta.env.VITE_*` in das Bündel. Ein Bild mit
eingebackenen Adressen kann nicht in zwei Umgebungen dasselbe sein. `web/src/env.ts`
löst deshalb in drei Schritten auf — `window.__WT_CONFIG__` → `VITE_*` →
Port-Rückfall — und `web/public/config.js` ist ein leeres Objekt, das lokal
nichts ändert. Nur das Helm-Chart legt ein echtes darüber. Das nicht zu einem
Bauargument „vereinfachen".
