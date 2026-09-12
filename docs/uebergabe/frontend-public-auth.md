# Übergabe — `features/public` und `features/auth` (apps/web)

Stand: 02.09.2026, **`features/public` ist fertig und grün** (15 Tests).
Territorium: ausschliesslich `apps/web/src/features/public/**` und
`apps/web/src/features/auth/**` sowie diese Datei. Nur `pnpm`/`npx`, kein
Docker, kein `dotnet`.

> **Schreib diese Datei nach JEDER fertigen Datei fort**, nicht nach jeder
> Seite. Der Rechner bricht gerade reihenweise weg (Akku); von vier Agenten
> haben zwei ihre gesamte Lesephase mitgenommen.

## 0. Stand — was liegt, was fehlt

| Datei | Stand |
|---|---|
| `public/test/render.tsx` | **fertig** (Vorgänger). Brauchbar, unverändert übernommen. |
| `public/pages/HomePage.tsx` | **fertig** (Vorgänger). Brauchbar, unverändert übernommen. |
| `public/pages/HomePage.test.tsx` | **fertig** — 6 Tests, grün. |
| `public/api/aufgaben.ts` | **fertig** — die vier Lesezugriffe der Übersicht. |
| `public/pages/OverviewPage.tsx` | **fertig**. |
| `public/pages/OverviewPage.test.tsx` | **fertig** — 9 Tests, grün. |
| `public/routes.tsx` (`publicRoutes`) | **fertig** — `index` → Home, `/overview`. |
| `auth/test/render.tsx` | **fertig** — wörtliche Kopie der Hülle aus `public/`, nur Importpfade angepasst. |
| `auth/api/registrierung.ts` | **fertig** — `registriere` · `bestaetigeEmail` · `sendeBestaetigungErneut` · `isPublicEmailDomain`. |
| `auth/pages/LoginPage.tsx` (+ Test) | **fertig** — 8 Tests, grün. Gegenprobe gefahren (Rückfalltext raus → genau der eine Test fällt). |
| `auth/pages/RegisterPage.tsx` (+ Test) | **fertig** — 15 Tests, grün. Radio-Hinweise stehen NEBEN der Beschriftung (aria-describedby), damit der Name exakt „Für ein Unternehmen" bleibt. |
| `auth/pages/VerifyPage.tsx` (+ Test) | offen — **hier weitermachen** |
| `auth/api/einladung.ts` + `pages/InvitationPage.tsx` (+ Test) | offen |
| `auth/pages/LogoutPage.tsx` (+ Test) | offen |
| `auth/routes.tsx` (`authRoutes`) | offen — **zuletzt**, sonst ist tsc rot |

Schon vorhanden und nicht von uns: `auth/store/authSlice.ts`,
`auth/store/authThunks.ts`, `auth/types/session.ts`, `auth/components/AuthCard.tsx`,
`auth/components/AuthModeTabs.tsx`. Erweitern erlaubt, umdrehen nicht.

`npx vitest run src/features/public src/features/auth` → **38 grün** (15 public, 23 auth).

## 1. Reihenfolge, in der gebaut wird

1. ~~`features/public/test/render.tsx`~~
2. ~~`features/public/pages/HomePage.tsx` (+ Test)~~
3. ~~`features/public/api/aufgaben.ts` + `pages/OverviewPage.tsx` (+ Test)~~
4. ~~`features/public/routes.tsx`~~
5. ~~`features/auth/test/render.tsx`~~
6. ~~`features/auth/api/registrierung.ts`~~
7. ~~`features/auth/pages/LoginPage.tsx` (+ Test)~~
8. ~~`features/auth/pages/RegisterPage.tsx` (+ Test)~~
9. `features/auth/pages/VerifyPage.tsx` (+ Test) ← **hier**
10. `features/auth/api/einladung.ts` + `pages/InvitationPage.tsx` (+ Test)
11. `features/auth/pages/LogoutPage.tsx` (+ Test)
12. `features/auth/routes.tsx` (`authRoutes`)

## 2. Quellen und was daraus wörtlich zu übernehmen ist

Alt → neu:

| alt | neu |
|---|---|
| `src/routes/home.tsx` (+ `home.css`) | `features/public/pages/HomePage.tsx` ✔ |
| `src/routes/overview.tsx` | `features/public/pages/OverviewPage.tsx` ✔ |
| `src/routes/login.tsx` | `features/auth/pages/LoginPage.tsx` |
| `src/routes/register.tsx` | `features/auth/pages/RegisterPage.tsx` |
| `src/routes/verify.tsx` | `features/auth/pages/VerifyPage.tsx` |
| `src/routes/invitation.tsx` | `features/auth/pages/InvitationPage.tsx` |
| `src/auth/client.ts` | `features/auth/api/registrierung.ts` |
| `src/auth/team.ts` (nur `acceptInvitation`) | `features/auth/api/einladung.ts` |

### Beschriftungen, an denen E2E hängt (aus `apps/web/e2e/stack.ts`)

- `/register`: Radio **„Für ein Unternehmen"**, Feld **„Name des Unternehmens"**,
  `/E-Mail/i`, `/Passwort/i` (`.first()`), `/Anzeigename/i`, Knopf `/Registrieren/i`.
- `/verify`: Überschrift **„E-Mail bestätigt"** *buchstabengetreu* (`exact: true`),
  Überschrift `/Bestätigung fehlgeschlagen/i`, Text `` `${companyName} ist angelegt` ``,
  bei abgelehntem Unternehmen ein `role="alert"`.
- `/login`: `/E-Mail/i`, `/Passwort/i`, Knopf `/Anmelden/i`; Fehlschlag als `role="alert"`.
- Achtung: `login()` in `stack.ts` wartet danach auf `locator("summary", { hasText: "Mein Konto" })`.
  Die **neue** Kopfzeile (`shared/components/layout/SiteHeader.tsx`) rendert dafür
  einen `<Button>`, kein `<summary>` — E2E wird an dieser Stelle brechen.
  **Nicht unser Gebiet**, gehört in die E2E-Anpassung.

### Drahtformat — snake_case

`POST /auth/register` bekommt `email`, `password`, **`display_name`** und
**nur wenn gewollt** `company_name`. Kein `null` mitschicken (wäre eine Aussage,
die niemand gemacht hat), nie `tenant_id`. `POST /auth/verify-email` und
`POST /invitations/accept` bekommen `{ token }`, `POST /auth/resend-verification`
`{ email }`.

## 3. Verhalten, das übernommen wird (und warum es fragil ist)

- **Einmal-Token, modulweit entdoppelt.** `verify.tsx` und `invitation.tsx`
  halten je eine `Map<token, Promise<Result>>` *ausserhalb* der Komponente. Grund
  ist gemessen: der Token ist einmalig, ein zweiter Aufbau (StrictMode, HMR,
  Reload) verbrennt ihn, und die zuletzt eintreffende Antwort entscheidet, was
  die Person sieht — im schlechten Fall „fehlgeschlagen" über einem
  freigeschalteten Konto. Ein `useRef` reicht nicht. **Muss so bleiben.**
- **Registrieren und erneut senden antworten für bekannte und unbekannte
  Adressen gleich** — kein „gibt es schon"-Zweig.
- **Freemail-Prüfung nur beim Beanspruchen einer Domain** (`art === "company"`),
  und erst, wenn überhaupt ein `@` dasteht. Die Absage spricht weiterhin der
  Server aus (422); die Oberfläche macht sie nur früh sichtbar.
- **`/verify` kennt drei Erfolge**: bestätigt · bestätigt mit Unternehmen ·
  bestätigt **ohne** Unternehmen samt Grund (`domain_already_claimed`). Ohne den
  dritten wäre die Seite grün, während die halbe Absicht verpufft ist.
- **`/overview` zählt nur, was auf eine Entscheidung wartet** — und bei einer
  gescheiterten Abfrage wird nichts gezählt, sondern gesagt, dass das Bild
  unvollständig ist. „Nichts liegt an" ist die eine Aussage, die dann falsch wäre.
  Singular/Plural sind ausformuliert („1 Gespräch" vs. „2 Gespräche"). ✔ umgesetzt
- **`/` leitet Angemeldete auf `/overview`** und rendert währenddessen nichts. ✔

## 4. Entscheidungen, die jemand anders treffen würde

1. **Kein TanStack Query mehr.** `AppRoot.tsx` hängt keinen `QueryClientProvider`
   ein, und `core/store/store.ts` (fremdes Gebiet) meldet nur `auth` und
   `preferences` an — dort darf **kein Slice ergänzt** werden. `/overview` liest
   deshalb mit lokalem Zustand über `core/api/client.request()` statt über einen
   Slice. Wer später einen `overview`-Slice will, braucht eine Änderung an
   `core/store/store.ts`.
2. **`/overview` bringt seine vier Lesezugriffe selbst mit**
   (`features/public/api/aufgaben.ts`): `GET {TRANSFER}/market/me/requests`,
   `GET {RESUME}/resumes/me/requests`, `GET {TRANSFER}/transfers/me`,
   `GET {TRANSFER}/transfers` (nur mit Firma). Die alten Clients
   (`src/market/client.ts` usw.) gehören `features/person`/`features/work` und
   damit anderen Agenten — ein Import dorthin wäre eine Kopplung an Code, den
   dieser Bereich nicht sieht. Preis: die Zählregeln stehen zweimal, bis die
   anderen Bereiche stehen.
3. **Der Rückweg zur gemerkten Stelle nach dem Anmelden entfällt vorerst.**
   Alt: `window.location.href = "/jobs/<id>/apply"` aus `src/jobs/intent.ts`,
   plus der Hinweis `ZurueckHinweis`. Das Modul gehört `features/work`; es hier
   nachzubauen hiesse, denselben `localStorage`-Schlüssel samt UUID-Prüfung und
   24-h-Verfall zweimal zu pflegen. Statt dessen: nach erfolgreicher Anmeldung
   `navigate("/overview")`, und im `LoginPage.tsx` steht ein Kommentar mit der
   genauen Einhängestelle. **Keine E2E-Reise hängt daran** (geprüft: die Reisen
   klicken „Bewerben" angemeldet).
4. **Abmelden ist ehrlich statt bequem.** Alt räumte die Sitzung in `onSettled`
   weg, also auch bei einem Netzfehler — das behauptet „abgemeldet", während das
   httpOnly-Cookie weiterlebt. Neu: bei Fehlschlag Fehlerblock und „Erneut
   versuchen"; der `authSlice` wird dafür **nicht** um `logout.rejected` erweitert.
5. **Erneut senden meldet auch einen Nicht-2xx als Fehlschlag.** Alt fing nur den
   Transportfehler ab; ein `429` von der Bremse (`/auth/resend-verification`:
   3/min) erzeugte die Zusage „ist unterwegs", obwohl nichts unterwegs war. Kein
   Aufzählungskanal, weil die Antwort für bekannte und unbekannte Adressen
   dieselbe ist. Die beiden alten Tests bleiben grün.
6. **Die Claim-Sätze der alten zweispaltigen `AuthLayout` wandern in den `lead`
   von `AuthCard`** (z. B. „Wechseln ist eine Entscheidung, kein Zufall. Du
   bestimmst, wer dich sieht, wer dich anspricht und was du teilst."), statt
   ersatzlos zu verschwinden. Das Markenpanel selbst gibt es nicht mehr.
7. **Testhülle je Bereich doppelt** (`features/public/test/render.tsx` und
   `features/auth/test/render.tsx`). Sie gehörte nach `src/shared/test/`, und
   das ist fremdes Gebiet. Die Hülle in `public/` ist fertig und lässt sich
   für `auth/` **wörtlich kopieren** (nur die Importpfade `../../auth/...`
   werden zu `../store/...` bzw. `../types/...`).
8. **`/overview` verlässt sich selbst, wenn niemand angemeldet ist**
   (`<Navigate to="/login" replace />`). Es gibt in `shared/` kein `RequireAuth`,
   und „Gerade wartet nichts auf dich" wäre für eine abgemeldete Besucherin
   keine leere Übersicht, sondern eine falsche Auskunft: der Satz spräche über
   das fehlende Token, nicht über die Person. Bei `status === "unknown"` steht
   ein `LoadingBlock` — nicht die Weiterleitung, sonst fliegt jeder Kaltstart
   auf `/login`.
9. **Die Firmenliste wird ohne aktives Unternehmen gar nicht gefragt.** Der
   Dienst antwortete 403, und ein Fehler, den die Anfrage selbst erzeugt hat,
   würde die Übersicht fälschlich als „unvollständig" markieren. Ein Test hält
   fest, dass `/transfers` dann nicht in den gestellten Anfragen vorkommt.

## 5. Fallen, über die schon jemand gestolpert ist

- **MUI 9:** `<Stack>` nimmt `alignItems`/`justifyContent` nicht mehr als Prop
  (gehört in `sx`); `Switch`/`Radio` haben kein `inputProps` mehr, sondern
  `slotProps={{ input: … }}`. `<Grid>` wird ganz gemieden zugunsten von
  `Box sx={{ display: "grid" }}`.
- **`required` malt bei MUI ein `*` in das `<label>`.** `getByLabelText("E-Mail")`
  (exakt) findet dann nichts mehr. Die Tests suchen deshalb mit `/E-Mail/i`,
  wie E2E es ohnehin tut (Playwrights `getByLabel` matcht standardmässig als
  Teilstring, „Name des Unternehmens" bleibt also gültig).
- **MUI `Alert` trägt immer `role="alert"`.** Die Zusage „Falls nötig, ist die
  E-Mail erneut unterwegs." muss `role="status"` bekommen — ein alter Test hält
  ausdrücklich fest, dass dort *kein* `alert` steht (eine Bestätigung
  unterbricht nicht).
- **`login`-Thunk kann ohne `ApiError` scheitern:** er `unwrap()`t intern
  `loadSession`, und dann ist `action.payload` undefiniert und der Slice setzt
  `error = null` — ein Knopf, der sichtbar nichts tut. `LoginPage` braucht dafür
  einen eigenen Rückfalltext.
- **Deutsche Anführungszeichen im Testnamen brechen den Parser.** `„…"` endet
  mit einem geraden `"`, das den JS-String schliesst: `[PARSE_ERROR] Expected
  ',' or ')'`, und die ganze Datei zählt als **0 Tests** — sieht in der Ausgabe
  aus wie eine übersprungene Datei, nicht wie ein Fehler. In `it(...)`-Titeln
  einfache Anführungszeichen benutzen.
- **`endsWith`, nicht `includes`, beim Antworten nach Pfad im Test.**
  `/transfers` ist ein Teilstring von `/transfers/me`; mit `includes` bekäme die
  eigene Liste die Antwort der Firmenliste, und der Test wäre grün, ohne etwas
  zu belegen. (In `OverviewPage.test.tsx` wird gleich `new URL(url).pathname`
  exakt verglichen.)
- **Widerspruch, der nicht aufgelöst wird:** `shared/components/layout/SessionGate.tsx`
  begründet ausdrücklich, dass die Marketingseite nicht auf die Sitzung warten
  soll; die alte App rendert auf `/` währenddessen `null`, um das Aufblitzen der
  Werbung vor der Weiterleitung zu vermeiden. Übernommen ist das alte Verhalten
  (nichts rendern, solange `status === "unknown"`), weil ein Sprung Werbung →
  Übersicht nach kaputt aussieht.

## 6. Prüfen

```bash
cd apps/web
npx tsc --noEmit                              # Fehler aus features/person|work|company sind FREMD
npx vitest run src/features/public src/features/auth
```
Nicht `pnpm build` — das prüft die ganze App, und die anderen Bereiche fehlen noch.
