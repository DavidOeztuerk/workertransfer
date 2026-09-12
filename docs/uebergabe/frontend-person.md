# Übergabe — `features/person` (apps/web)

**Territorium:** ausschliesslich `apps/web/src/features/person/**` und diese
Datei. Kein Docker, kein `dotnet` — nur `pnpm`/`npx` in `apps/web`.

**Stand:** 02.09.2026 — **1 von 9 fertig: `/consents`.** 11 Tests grün.
Diese Datei wird nach *jeder* fertigen Seite fortgeschrieben.

---

## 1. Die neun Seiten und die Reihenfolge

| # | Pfad | alte Quelle | Stand |
|---|---|---|---|
| 1 | `/consents` | `routes/consents.tsx`, `consent/client.ts` | **fertig** (11 Tests) |
| 2 | `/profile` | `routes/profile.tsx`, `profile/client.ts`, `profile/DraftHelp.tsx`, `skills.ts` | offen |
| 3 | `/resume` | `routes/resume.tsx`, `resume/client.ts` | offen |
| 4 | `/portfolio` | `routes/portfolio.tsx`, `portfolio/client.ts` | offen |
| 5 | `/portfolio/new`, `/portfolio/:index` | `routes/portfolio-item.tsx` | offen |
| 6 | `/github` | `routes/github.tsx`, `github/client.ts` | offen |
| 7 | `/my-data` | `routes/my-data.tsx`, `export/build.ts` | offen |
| 8 | `/settings` | `routes/settings.tsx`, `settings/client.ts` | offen |
| 9 | `/delete-account` | `routes/**account-deletion**.tsx`, `account/client.ts` | offen |

**Der Auftrag nennt acht Seiten; es sind neun.** `/portfolio/new` und
`/portfolio/:index` sind eine **eigene Route** mit eigener alter Quelle
(`routes/portfolio-item.tsx`) und eigenem alten Test. Die Liste auf
`/portfolio` verlinkt sie und bietet selbst **kein** Entfernen an — das ist
Absicht (siehe §4). Ohne sie wäre `/portfolio` eine Liste ohne Formular.

**Warum diese Reihenfolge.** `/consents` zuerst: dort entstehen der Umgang mit
dem Ledger und die drei Sätze „leer" / „nicht freigegeben" / „Ledger schweigt".
`/profile` als zweites, weil dort `ConsentSwitch` zum ersten Mal steht und die
Formulierungshilfe hängt. `/my-data` erst nach 1–6, weil es alle deren
Lesezugriffe bündelt. `/delete-account` zuletzt: heikelste Seite, und sie
profitiert von der Sitzungsbehandlung aller anderen.

**Mittendrin:** nichts. `features/person/` ist leer.

---

## 2. Aufbau

```
src/features/person/
  api/{consent,profile,resume,portfolio,github,settings,account,companies,export}.ts
  lib/useAsync.ts                 Laden ohne TanStack Query
  test/{render.tsx,netz.ts}       Store+Theme+Router · fetch-Attrappe
  pages/*.tsx (+ .test.tsx)       neun Seiten
  components/DraftHelp.tsx        Formulierungshilfe (nur /profile)
  routes.tsx  ->  export const personRoutes
```

### Zwei Befunde am Fundament, die den Aufbau erzwingen

1. **Kein `QueryClientProvider`.** `AppRoot.tsx` ist `ThemeProvider` +
   `RouterProvider`. `useQuery` aus dem alten Code würde in der echten
   Anwendung werfen. Der ALTE Testhelfer `src/test/render.tsx` stellt einen
   bereit — ein Test wäre also grün, während die Seite weiss bleibt.
2. **Der Store nimmt keine neuen Slices auf.** `core/store/store.ts` meldet
   `auth` und `preferences` an und ist fremdes Territorium. Ein `personSlice`
   wäre nirgends registriert.

**Folge:** Laden über `lib/useAsync.ts` (`useState` + `useEffect` +
`AbortController`) über `request()` aus `core/api/client.ts`. Die Sitzung kommt
aus `state.auth`. `status === "unknown"` ist ein **Ladezustand**, nicht
„abgemeldet" — sonst blitzt auf jeder dieser Seiten „Bitte anmelden" auf, und
auf `/delete-account` wäre das der schlimmste Satz überhaupt.

`useAsync` und die Testhülle stehen je Feature einmal; sie gehörten nach
`src/shared/`, und das ist fremdes Gebiet. Beim Zusammenlegen: dorthin ziehen.

---

## 3. Die Draht-Endpunkte, die dieser Bereich benutzt

| Modul | Dienst (env) | Pfade |
|---|---|---|
| `api/consent.ts` | `CONSENT_BASE_URL` | `POST /consent/{check,grant,revoke}`, `GET /consent/me`, `GET /consent/me/history` |
| `api/profile.ts` | `PROFILE_BASE_URL` | `GET/PUT /profiles/me`, `POST /profiles/me/draft` |
| `api/resume.ts` | `RESUME_BASE_URL` | `GET/PUT /resumes/me`, `GET /resumes/me/requests`, `POST /resumes/requests/{id}/{grant,decline,revoke}` |
| `api/portfolio.ts` | `PORTFOLIO_BASE_URL` | `GET/PUT /portfolios/me`, `POST /portfolios/me/attachments`, `GET /portfolios/{sub}/attachments/{name}` |
| `api/github.ts` | `GITHUB_BASE_URL` | `GET/POST/DELETE /github/me`, `POST /github/me/{verify,refresh}` |
| `api/settings.ts` | `API_BASE_URL` | `GET/PUT /me/notification-preferences` |
| `api/account.ts` | `API_BASE_URL` | `POST /account/erasure` (kein Rumpf) |
| `api/companies.ts` | `COMPANIES_BASE_URL` | `GET /companies/{tenantId}` (nur der Anzeigename für `/consents`) |
| `api/export.ts` | mehrere | zusätzlich `GET /me`, `GET /market/me`, `GET /market/me/requests`, `GET /transfers/me` (alle `TRANSFER_BASE_URL`), `GET /applications/me` (`APPLICATIONS_BASE_URL`) |

**snake_case, wörtlich:** `subject_id`, `remote_ok`, `display_name`,
`granted_at`, `recorded_at`, `started_on`, `ended_on`, `challenge_description`,
`fetched_at`, `pushed_at`, `next_cursor`, `resume_request`, `market_request`,
`application_update`, `transfer_update`, `content_type`, `is_approachable`,
`answered_at`, `tenant_id`, `created_at`.
Leeres Bis-Datum als `null`, nie `""`. Leerer Link/leeres Jahr als `null`.

**`GITHUB_BASE_URL` zeigt in `src/env.ts` auf Port 8010** — das ist
notification-service; github-service läuft auf **8011**. `env.ts` ist fremdes
Territorium: **nicht angefasst, hiermit erneut gemeldet** (der work/company-
Strom hat denselben Fund).

---

## 4. Verhalten, das wörtlich übernommen wird

- **Freigabeschalter ist ein Schalter** (`ConsentSwitch`), keine Ankreuzbox.
  Auf `/profile`, `/portfolio` und `/settings`. Kein Speichern-Knopf daneben.
- **Drei Sätze, nie zwei.** `null` aus `isGranted` heisst „der Ledger hat nicht
  geantwortet": Anzeige bleibt AUS **und** der Schalter wird **gesperrt**, mit
  eigenem Satz (`/nicht abrufbar/i`) — sonst schickt der nächste Klick ein
  `grant` für eine Einwilligung, deren Stand niemand kennt. Dasselbe für
  `/settings` (`null` statt erfundenem `ALL_ON`) und für jede Liste: ein Fehler
  wird **nie** als leere Liste gezeigt.
- **Reihenfolge auf jeder Liste: lädt → Fehler → leer → Inhalt.** Der
  Ladezustand fehlte im alten Code bei den Lebenslauf-Anfragen und sah aus wie
  „hat niemand gefragt".
- **`/consents` zeigt eine unbekannte Capability roh an**, statt sie zu
  verschlucken — und lässt sie trotzdem zurückziehen. Der Widerruf trägt immer
  einen Grund (`expect.stringContaining("Freigaben")`).
- **Firmennamen kommen frisch von companies-service**, nie aus dem Ledger.
  Ohne Profil steht „Ein Unternehmen", nie die UUID.
- **`/resume`: `status` sagt, was geschah; `active` sagt, was gilt.**
  „Zurückziehen" nur bei `GRANTED && active === true`, sonst `undefined` statt
  eines abgeschalteten Knopfes. Abgelehnt bleibt abgelehnt.
- **`/portfolio`: kein Entfernen in der Liste** — es steht auf der Seite der
  einzelnen Arbeit, wo die Person sie vor sich hat.
- **`/portfolio/:index`: der Titel steht in der `<h1>`.** Die Adresse ist die
  Stelle im Feld (`PortfolioItem` hat keine ID); die Überschrift ist die
  Gegenmassnahme. Eine Adresse ohne Arbeit ergibt „Diese Arbeit gibt es nicht",
  **kein** leeres Formular. Gespeichert wird immer das **ganze** Feld.
- **Anhang: der lokale Dateiname wird nie angezeigt** — er geht nicht zum
  Server. Hochgeladen sofort, gespeichert mit dem Formular.
- **`/github`: Belege, keine Noten.** Kein Abgleich im Hintergrund. Ausfall
  (503) und fehlender Gist (422) bleiben getrennt — ein Ausfall darf nicht wie
  ein fehlender Nachweis aussehen. Kein Formular „Konto nennen", solange
  geladen wird.
- **`/my-data`: ein gescheiterter Abschnitt wird NICHT weggelassen**, er bleibt
  in der Datei und sagt „nicht_abrufbar". Vor dem Herunterladen steht, was
  fehlt. Löschen ist ein **Verweis**, nie ein Nachbarknopf.
- **`/settings`: jeder Schalter wirkt sofort**, kein Speichern-Knopf. Der
  Fehlschlag zeigt wieder, was **gilt**, nicht was gewollt war.
- **`/delete-account`:** alles aus §3 des Auftrags, plus der Absatz über
  Unternehmen (nur wenn es welche gibt), plus: kein Wort über
  Wiederherstellung/Frist, keine Aufbewahrungszusage, kein Eingabefeld, kein
  `progressbar`, und **der Erfolgszustand vor der Anmeldeaufforderung**.

---

## 5. Bewusste Abweichungen vom alten Code (jede mit Grund)

1. **Kein leeres Formular über einem gescheiterten Abruf.** Alt gab
   `getMyProfile`/`getMyResume`/`getMyPortfolio` bei **jedem** Nicht-2xx `null`
   zurück, und `null` ist dort „noch keins angelegt". Wer dann tippt und
   speichert, überschreibt Überschrift, Text, Ort und Fähigkeiten mit leer.
   Neu: „kein Profil" (200 mit `null`, oder 404) und „nicht ladbar" sind
   getrennt; bei „nicht ladbar" steht ein `ErrorBlock` und **kein** Formular.
   Derselbe Befund, den der work/company-Strom für `/market` und
   `/company/profile` festgehalten hat.
2. **Tests stubben `fetch` statt der Client-Module.** Damit prüft jeder Test
   auch den **Draht** (Pfad, Methode, snake_case im Rumpf) — genau die Ebene,
   auf der `remote_ok`/`remoteOk` verlorenging.
3. **Der dritte Sitzungszustand.** Die alten Seiten bekamen `principal` als
   Prop; `unknown` gab es nicht. Neu kommt die Sitzung aus `state.auth`, und
   `unknown` wird als Ladezustand gezeichnet.
4. **`/my-data` bringt seine Lesezugriffe selbst mit** (`api/export.ts`), statt
   `src/market/client.ts`, `src/transfers/client.ts`,
   `src/applications/client.ts` zu importieren: die gehören `features/work` und
   damit einem anderen Agenten. Preis: die Pfade stehen zweimal, bis die
   Bereiche zusammengelegt werden.
5. **Navigation über `react-router-dom`**, nicht `window.location.href`. Alt
   sprang `portfolio-item.tsx` per `location.href = "/portfolio"` — ein voller
   Neuladevorgang mitten in der Anwendung.

---

## 6. Fallen (gemessen, teils von den Schwesterströmen)

- **MUI 9:** `<Stack>` nimmt `alignItems`/`justifyContent` nicht mehr als Prop
  (in `sx`); `Switch` hat kein `inputProps`, sondern `slotProps={{ input: … }}`.
- **`required` malt bei MUI ein `*` ins `<label>`** — `getByLabelText("Ort")`
  exakt findet dann nichts. Die alten Tests suchen ohnehin mit `/Ort/i`.
- **MUI `Alert` trägt immer `role="alert"`.** Bestätigungen („Profil
  gespeichert.", der Löschhinweis) brauchen `role="status"` — die alten Tests
  suchen dafür ausdrücklich `findByRole("status")`.
- **`getByLabelText(/Ort/i)` trifft auch „Wort"-Teile.** Auf `/profile` sind
  „Überschrift", „Über mich", „Ort", „Fähigkeiten" nah beieinander; Hinweistexte
  dürfen die Labels nicht doppeln.
- **Erfolgsmeldungen wörtlich prüfen.** `/gespeichert/i` traf auch den Hinweis
  der Formulierungshilfe („Gespeichert wird nichts").

---

## 7. Prüfen

```bash
cd apps/web
npx tsc --noEmit            # Fehler aus features/{public,auth,work,company} sind FREMD
npx vitest run src/features/person
```
Nicht `pnpm build` — die anderen Bereiche fehlen noch.


---

## 8. Fortschritt

### `/consents` — fertig (02.09., 11 Tests)

Dateien: `api/consent.ts`, `api/companies.ts`, `pages/ConsentsPage.tsx(+.test)`,
dazu die Grundlage `lib/{useAsync,session}.ts`, `test/{render.tsx,netz.ts}`,
`components/AnmeldungNoetig.tsx`.

Übernommen: alle acht Zusagen der alten Testreihe — Anmeldung statt fremder
Liste, „Niemand sieht etwas von dir", Fehler nie als leere Liste, Bereichsname
plus „Alle Unternehmen", aufgelöster Firmenname statt UUID, „Ein Unternehmen"
ohne Profil, unbekannte Capability roh **und** zurückziehbar, Widerruf mit
Begründung, gescheiterter Widerruf als `alert`.

Dazu zwei neue Zusagen: **`unknown` wird als Ladezustand gezeichnet** (den
Zustand gab es im alten Code nicht), und **`GET /consent/me` trägt die
Subjektkennung nirgends** — weder im Pfad noch in der Abfrage. Der zweite ist
nur möglich, weil die Tests `fetch` stubben statt der Client-Module.

**Gegenprobe gefahren** (beide fielen, danach zurückgenommen):
Leerzustand auch bei Fehler zeigen → rot; Widerruf ohne Begründung → rot.

**Alle Ergebnisse tragen jetzt einen `ApiError`** statt einer Zeichenkette.
Damit zeichnet `ErrorBlock` aus dem gemeinsamen Set auch die
Korrelationskennung — der einzige Faden, an dem sich eine Beschwerde durch alle
Dienste zurückverfolgen lässt.
