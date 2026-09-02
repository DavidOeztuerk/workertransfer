# Übergabe — `features/public` und `features/auth` (apps/web)

Stand: 02.09.2026, nach der Lesephase, **vor** der ersten angelegten Datei.
Territorium: ausschliesslich `apps/web/src/features/public/**` und
`apps/web/src/features/auth/**`. Nur `pnpm`/`npx`, kein Docker, kein `dotnet`.

## 1. Was schon liegt (nicht von mir)

- `features/auth/store/authSlice.ts`, `store/authThunks.ts`, `types/session.ts`,
  `components/AuthCard.tsx`, `components/AuthModeTabs.tsx` — dürfen erweitert,
  aber nicht umgedreht werden.
- `features/public/` ist **leer**.
- Von mir angelegt: **noch nichts.** `npx tsc --noEmit` wurde deshalb noch nicht
  sinnvoll gefahren (es fiele ohnehin über `features/person|work|company`, deren
  `routes.tsx` der Router schon importiert — fremdes Gebiet).

## 2. Reihenfolge, in der ich baue

1. `features/public/test/render.tsx` — Provider-Hülle (Store + Theme + MemoryRouter)
2. `features/public/pages/HomePage.tsx` (+ Test)
3. `features/public/api/aufgaben.ts` + `pages/OverviewPage.tsx` (+ Test)
4. `features/public/routes.tsx` (`publicRoutes`)
5. `features/auth/test/render.tsx`
6. `features/auth/api/registrierung.ts` (register · verify · resend · Freemail)
7. `features/auth/pages/LoginPage.tsx` (+ Test)
8. `features/auth/pages/RegisterPage.tsx` (+ Test)
9. `features/auth/pages/VerifyPage.tsx` (+ Test)
10. `features/auth/api/einladung.ts` + `pages/InvitationPage.tsx` (+ Test)
11. `features/auth/pages/LogoutPage.tsx` (+ Test)
12. `features/auth/routes.tsx` (`authRoutes`)

## 3. Quellen und was daraus wörtlich zu übernehmen ist

Alt → neu:

| alt | neu |
|---|---|
| `src/routes/home.tsx` (+ `home.css`) | `features/public/pages/HomePage.tsx` |
| `src/routes/overview.tsx` | `features/public/pages/OverviewPage.tsx` |
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
  **Nicht mein Gebiet**, gehört in die E2E-Anpassung.

### Drahtformat — snake_case

`POST /auth/register` bekommt `email`, `password`, **`display_name`** und
**nur wenn gewollt** `company_name`. Kein `null` mitschicken (wäre eine Aussage,
die niemand gemacht hat), nie `tenant_id`. `POST /auth/verify-email` und
`POST /invitations/accept` bekommen `{ token }`, `POST /auth/resend-verification`
`{ email }`.

## 4. Verhalten, das ich übernehme (und warum es fragil ist)

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
  Singular/Plural sind ausformuliert („1 Gespräch" vs. „2 Gespräche").
- **`/` leitet Angemeldete auf `/overview`** und rendert währenddessen nichts.

## 5. Entscheidungen, die jemand anders treffen würde

1. **Kein TanStack Query mehr.** `AppRoot.tsx` hängt keinen `QueryClientProvider`
   ein, und `core/store/store.ts` (fremdes Gebiet) meldet nur `auth` und
   `preferences` an — ich darf dort **keinen Slice ergänzen**. `/overview` liest
   deshalb mit lokalem Zustand über `core/api/client.request()` statt über einen
   Slice. Wer später einen `overview`-Slice will, braucht eine Änderung an
   `core/store/store.ts`.
2. **`/overview` bringt seine vier Lesezugriffe selbst mit**
   (`features/public/api/aufgaben.ts`): `GET {TRANSFER}/market/me/requests`,
   `GET {RESUME}/resumes/me/requests`, `GET {TRANSFER}/transfers/me`,
   `GET {TRANSFER}/transfers` (nur mit Firma). Die alten Clients
   (`src/market/client.ts` usw.) gehören `features/person`/`features/work` und
   damit anderen Agenten — ein Import dorthin wäre eine Kopplung an Code, den ich
   nicht sehe. Preis: die Zählregeln stehen zweimal, bis die anderen Bereiche
   stehen.
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
   das ist fremdes Gebiet.

## 6. Fallen, über die ich schon gestolpert bin

- **MUI 9:** `<Stack>` nimmt `alignItems`/`justifyContent` nicht mehr als Prop
  (gehört in `sx`); `Switch`/`Radio` haben kein `inputProps` mehr, sondern
  `slotProps={{ input: … }}`. `<Grid>` meide ich ganz zugunsten von
  `Box sx={{ display: "grid" }}`.
- **`required` malt bei MUI ein `*` in das `<label>`.** `getByLabelText("E-Mail")`
  (exakt) findet dann nichts mehr. Meine Tests suchen deshalb mit `/E-Mail/i`,
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
- **Widerspruch, den ich nicht auflöse:** `shared/components/layout/SessionGate.tsx`
  begründet ausdrücklich, dass die Marketingseite nicht auf die Sitzung warten
  soll; `src/app.tsx` rendert auf `/` währenddessen `null`, um das Aufblitzen der
  Werbung vor der Weiterleitung zu vermeiden. Ich übernehme das alte Verhalten
  (nichts rendern, solange `status === "unknown"`), weil ein Sprung Werbung →
  Übersicht nach kaputt aussieht.

## 7. Prüfen

```bash
cd apps/web
npx tsc --noEmit                              # Fehler aus features/person|work|company sind fremd
npx vitest run src/features/public src/features/auth
```
Nicht `pnpm build` — das prüft die ganze App, und die anderen Bereiche fehlen noch.
