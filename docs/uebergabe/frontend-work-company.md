# Zwischenstand — Frontend `work` + `company`

**Territorium:** ausschliesslich `apps/web/src/features/work/**` und
`apps/web/src/features/company/**`. Kein Docker, kein `dotnet`, nur
`pnpm`/`npx` in `apps/web`.

**Stand:** 02.09.2026 — **Analyse vollständig, Code noch nicht geschrieben.**
Wer hier weitermacht, kann direkt anfangen: der Plan unten nennt jede Datei,
jede übernommene Regel und jeden gemessenen Widerspruch.

---

## 1. Seiten — fertig / offen

| Bereich | Pfad | alte Quelle | Stand |
|---|---|---|---|
| work | `/jobs` | `routes/jobs.tsx`, `jobs/client.ts`, `jobs/match.ts`, `jobs/Requirements.tsx` | offen |
| work | `/careers/<slug>` | `routes/career.tsx` (heisst **career**.tsx, nicht careers) | offen |
| work | `/jobs/:id/apply` | `routes/job-apply.tsx` | offen |
| work | `/applications` | `routes/applications.tsx`, `applications/client.ts` | offen |
| work | `/market` | `routes/market.tsx`, `market/client.ts` | offen |
| work | `/transfers` | `routes/transfers.tsx`, `transfers/client.ts` | offen |
| company | `/candidates` | `routes/candidates.tsx`, `candidates/CandidateCard.tsx` | offen |
| company | `/company/jobs` | `routes/company-jobs.tsx` | offen |
| company | `/company/jobs/new` | `routes/company-job-new.tsx` | offen |
| company | `/company/team` | `routes/**team.tsx**` (nicht `company-team.tsx`) | offen |
| company | `/company/team/invite` | `routes/company-team-invite.tsx` | offen |
| company | `/company/profile` | `routes/company-profile.tsx`, `companies/client.ts` | offen |
| company | `/company/transfers` | `routes/company-transfers.tsx` | offen |

**Reihenfolge, in der gearbeitet wird** (jede Seite mit `.test.tsx` daneben):
`/jobs` → `/jobs/:id/apply` → `/applications` → `/careers/<slug>` → `/market` →
`/transfers` → `/company/jobs` → `/company/jobs/new` → `/candidates` →
`/company/profile` → `/company/team` (+ `/invite`) → `/company/transfers`.

Zuerst `/jobs`, weil dort die vier tragenden Regeln zusammenkommen (Passung,
anonymer Zugang, Draht-Namen, Firmenprofil daneben) und die anderen Seiten
Bausteine daraus erben.

**Mittendrin:** nichts. Es ist noch keine Datei angelegt — es gibt nichts
Halbfertiges aufzuräumen.

---

## 2. Aufbau, der gelten soll

```
src/features/work/
  api/{jobs,applications,market,transfers,profile}.ts   Clients der Dienste
  lib/{match,intent,skills,useAsync,session}.ts
  components/Requirements.tsx
  pages/{JobsPage,JobApplyPage,ApplicationsPage,CareerPage,MarketPage,TransfersPage}.tsx (+ .test.tsx)
  routes.tsx        ->  export const workRoutes

src/features/company/
  api/{companies,candidates,resumeRequests,github,team}.ts
  lib/{useAsync,session}.ts
  components/CandidateCard.tsx
  pages/{CandidatesPage,CompanyJobsPage,CompanyJobNewPage,CompanyProfilePage,CompanyTeamPage,CompanyTeamInvitePage,CompanyTransfersPage}.tsx (+ .test.tsx)
  routes.tsx        ->  export const companyRoutes
```

**Wo ein Client liegt, entscheidet der Dienst, nicht die Seite.**
jobs-, applications-, transfer-Dienst → `work/api`; companies-, identity-
(Mannschaft) und die Kandidatenliste → `company/api`. Beide Seiten importieren
über den Schnitt, wo es fachlich nötig ist (`company/pages/CompanyJobsPage`
liest `work/api/jobs`; `work/pages/JobsPage` liest `company/api/companies` für
den Namen des Unternehmens). Das sind **keine Modulzyklen** — die `api/`-Module
importieren einander nicht.

### Zwei Befunde am neuen Fundament, die den Aufbau bestimmen

1. **Es gibt keinen `QueryClientProvider`.** `AppRoot.tsx` ist
   `ThemeProvider` + `RouterProvider`, sonst nichts. `useQuery` aus dem alten
   Code würde in der echten Anwendung werfen („No QueryClient set"). Der
   Testhelfer `src/test/render.tsx` stellt zwar einen bereit — ein Test wäre
   also grün, während die Seite in der Anwendung weiss bleibt. **TanStack Query
   ist deshalb nicht benutzbar**, solange `main.tsx`/`AppRoot.tsx` niemandem
   gehören.
2. **Der Store nimmt keine neuen Slices auf.** `src/core/store/store.ts`
   registriert `auth` und `preferences`; die Datei liegt ausserhalb jedes
   Feature-Territoriums. Ein `workSlice` wäre nirgends angemeldet, und
   `useAppSelector(s => s.work)` käme als `undefined` zurück.

**Folge:** die Seiten laden mit einem eigenen kleinen Haken (`useAsync`:
`useState` + `useEffect` + `AbortController`) über `request()` aus
`core/api/client.ts`. Der Sitzungszustand kommt aus dem Store
(`state.auth.session` / `state.auth.status`) — dort ist er schon.
`status === "unknown"` muss als **Ladezustand** gezeichnet werden, nicht als
„abgemeldet"; sonst blitzt auf jeder Seite kurz „Bitte anmelden" auf.

`useAsync` und der Sitzungshaken stehen **je einmal pro Feature** (acht bzw.
dreissig Zeilen). Sie gehören eigentlich nach `src/shared/hooks/` — das ist
fremdes Territorium. Beim Zusammenlegen der Ströme: dorthin ziehen, beide
Kopien löschen.

---

## 3. Was aus dem alten Code wörtlich übernommen wird

### Die vier tragenden Regeln

- **Keine Zahl über einen Menschen, keine Rangfolge.** `matchSkills`
  (`src/jobs/match.ts`) wandert unverändert nach `work/lib/match.ts`: Vergleich
  auf **Gleichheit** (nicht Enthaltensein — „Java" ist nicht „JavaScript"),
  `toLowerCase` statt `toLocaleLowerCase` (türkisches „ı" würde sonst über die
  Passung entscheiden), Schreibweise und Reihenfolge bleiben die der
  **Ausschreibung**. `Requirements.tsx` zeigt „Du hast 2 von 3 genannten
  Fähigkeiten:" plus Häkchenliste, **nie** einen Prozentwert. Drei Zustände:
  `mine === null` (nicht angemeldet oder Antwort steht aus) → gar keine
  Passungszeile; `mine === []` → „Trage Fähigkeiten in deinem Profil ein …",
  **nie „0 von 3"**; sonst abgleichen. Nennt die Stelle keine Fähigkeiten,
  steht gar nichts da. Gerechnet wird im Browser, gezeigt wird es **nur der
  Person** — auf `/jobs` und `/jobs/:id/apply`, auf **keiner** Seite unter
  `/company/**` und in **keiner** Kandidatenkarte.
- **Die Kandidatenliste liegt auf `GET /candidates`** (profile-service),
  niemals auf `/profiles` — der blanke Präfix ist eine absichtlich tote Tür
  (404 in allen drei Handlungsformen, so steht es in `docs/routenkarte.yml`).
  Genau dieser Fehler liess die Liste monatelang still scheitern. Die Filter
  gehen als `?skill=…&skill=…&location=…&remote=true` in die **URL**, nicht in
  den Cursor. `remote=false` wird gar nicht erst gesendet.
- **503 ist ein eigener Zustand, nie eine leere Liste.** Betroffen:
  `/candidates` (`consent-unavailable` → „Der Consent-Ledger antwortet gerade
  nicht. Wir zeigen lieber nichts als das Falsche.", und **keine** Liste
  daneben), `listMyMarketRequests`/`listCompanyMarketRequests`,
  `getMarketStatus`, die Bewerbung (`unavailable`) und die Transfer-Züge. 403
  heisst „du handelst für keine Firma" und ist eine Aussage über den Aufrufer:
  eigener Satz („Wechsle oben auf ein Unternehmen"), keine Fehlermeldung. 404
  bleibt ununterscheidbar von „nicht freigegeben" — die Oberfläche bastelt
  daraus keine Auskunft, die der Server gerade verweigert hat.
- **Der Bewerbungsfluss öffnet Daten, und das steht vor dem Knopf.**
  `shares_resume` (vorbelegt **an**) und `shares_portfolio` (vorbelegt **aus**)
  als **Kästchen, nicht als Schalter** — hier gilt die Freigabe erst mit dem
  Absenden. Das Profil ist **kein** Kästchen: „Dein Profil geht immer mit —
  ohne es wäre es keine Bewerbung." Nach dem Absenden steht dort, wo man es
  getan hat, wie man es zurücknimmt (Link auf `/applications`). Auf
  `/applications` nennt jede laufende Bewerbung, was offen ist
  („Freigegeben: Profil, Lebenslauf"), und „Zurückziehen" gibt es **nur**
  solange `submitted`/`reviewing` — für eine geschlossene Bewerbung `undefined`
  statt eines abgeschalteten Knopfes.

### Weitere Verhaltensweisen, die nicht verloren gehen dürfen

- **Kein Formular über einem gescheiterten Abruf.** `/market` und
  `/company/profile` zeigen bei `null`/`ok:false` **kein** Formular, sondern
  einen Satz. Grund gemessen: `toForm(null)` ist ein leeres Formular, der
  Ladezustand ist vorbei, und wer dann etwas tippt und speichert, überschreibt
  Notiz/Über-uns/Website/Standorte mit leer — auf `/market` verschwindet die
  Person sogar vom Markt (`availability: "unavailable"`).
- **Reihenfolge auf jeder Liste: lädt → Fehler → leer → Inhalt.** Der
  Ladezustand fehlte im alten Code an vier Stellen (`/applications`,
  `/company/jobs`, `/company/transfers`, Mannschaft) und hinterliess eine leere
  Karte.
- **Blättern behält die vorigen Seiten** (`/jobs`, `/candidates`), und
  `/candidates` nennt **keine Gesamtzahl** — sie verriete, wie viele Profile
  gerade *nicht* freigegeben sind.
- **Die gemerkte Stelle** (`jobs/intent.ts`) wandert mit: nur eine **UUID**
  wird gespeichert, nie ein Pfad (Open Redirect entsteht so gar nicht erst),
  24 Stunden gültig, `localStorage` (der Bestätigungslink landet in einem
  anderen Tab), Prüfung der ID auch beim **Lesen**, und ein Speicher, der
  Methoden vermissen lässt (Node ≥ 25), wirft nichts um.
- **Draht in snake_case**, wörtlich übernommen: `job_id`, `shares_resume`,
  `shares_portfolio`, `subject_id`, `tenant_id`, `created_at`, `remote_ok`,
  `next_cursor`, `fee_cents`, `start_on`, `requires_release`,
  `release_confirmed`, `display_name`, `is_approachable`, `answered_at`.
  Angebot: leerer Monat als `null`, nie `""` (`""` scheitert am Muster des
  Vertrags und liest sich wie ein Serverfehler). Euro → Cent:
  `Math.round(Number(fee) * 100)`.
- **Kein `tenant_id` im Rumpf** — weder beim Anlegen einer Stelle noch beim
  Firmenprofil. Das Unternehmen steht im Token; was der Client nicht senden
  kann, kann er nicht fälschen.
- **Ein Pfad je Übergang** bei Transfers (`accept-talk`, `accept-offer`,
  `confirm-release`, `decline`, `complete`, `withdraw`), kein `PATCH status`.
  „Abschliessen" bietet das Unternehmen **nur** an, wenn keine Freigabe nötig
  ist — sonst schliesst die Person selbst ab.
- **Das Fähigkeitsvokabular benennt um, es schliesst nie**: `parseSkills`
  trennt an Kommas, wirft Leeres weg, lehnt nichts ab und erfindet nichts. Der
  Hinweis am Feld nennt „Postgres"/„PostgreSQL" als Beispiel.
- **Kein Farbliteral.** Alles über `theme.palette.*`; Grün/Rot/Bernstein
  bleiben *erteilt / zurückgezogen / in Arbeit* vorbehalten.

---

## 4. Beschriftungen, die E2E sucht — wörtlich zu übernehmen

| Reise | Selektor | Seite |
|---|---|---|
| jobs, application | `getByLabel("Titel")` | `/company/jobs/new` |
| jobs, application | `getByLabel(/Beschreibung/i)` | `/company/jobs/new` |
| jobs | `getByLabel("Ort", { exact: true })` | `/company/jobs/new` |
| — | `getByLabel(/Gesuchte Fähigkeiten/i)` | `/company/jobs/new` |
| jobs, application | `getByRole("button", { name: /Entwurf anlegen/i })` | `/company/jobs/new` |
| jobs | `getByLabel(/Suchbegriff/i)`, `button /Suchen/i`, `/nichts gefunden/i` | `/jobs` |
| jobs | `button /Veröffentlichen/i`, `/Schließen/i`; Text „Entwurf", „Veröffentlicht", „Geschlossen"; Zeile ist ein `li` | `/company/jobs` |
| jobs | `getByRole("heading", { name: companyName })` | `/careers/<slug>` |
| team | `getByLabel(/E-Mail/i)` | `/company/team/invite` |
| transfer | `radio /Ich höre zu/`, `/Ich suche aktiv/`; `checkbox /Ich arbeite gerade irgendwo/`; `button /^Speichern$/`; Text `/Marktstatus gespeichert/i` | `/market` |
| transfer | `button /Marktstatus anfragen/i`, `/Interesse zeigen/i`; Text `/Marktstatus angefragt/i`, `/Hört zu/`, `/Interesse hinterlegt/i` | `/candidates` |
| transfer | `heading /Ein Unternehmen hat Interesse/`, `/Ihr seid im Gespräch/`, `/^Abgeschlossen$/`; `button /Gespräch annehmen/i`, `/Angebot annehmen/i`, `/Freigabe bestätigen und abschließen/i`, `/Ablehnen/i`; Text `5.000,00 €` | `/transfers` |
| transfer | `getByLabel(/^Angebot/)`, `/^Start/`, `/Ablöse in Euro/`; `button /Angebot machen/i`; `heading /Angebot abgegeben/`; Text `/Braucht eine Freigabe/` | `/company/transfers` |
| consents | `getByLabel("Fähigkeiten")`, `getByLabel(/Ort/i)` | `/candidates` |

Radios und Kästchen müssen **echte** `role="radio"`/`role="checkbox"` sein
(MUI `Radio`/`Checkbox` sind es), und die Euro-Ausgabe bleibt
`Intl.NumberFormat("de-DE", { style: "currency", currency: "EUR" })` —
`5.000,00 €` steht so in der Reise.

**MUI-9-Fallen:** `<Stack>` nimmt `alignItems`/`justifyContent` nicht mehr als
Prop (in `sx`, oder gleich `<Box sx={{ display: "flex" }}>`); `Switch` hat kein
`inputProps`, sondern `slotProps={{ input: … }}`. Für ein Auswahlfeld, das
Playwright mit `selectOption` bedienen könnte, braucht es ein natives
`<select>` (`TextField select` + `slotProps={{ select: { native: true } }}`) —
MUIs Voreinstellung ist ein Listenfeld aus `div`s.

---

## 5. Widersprüche zwischen altem Code, alten Tests und E2E

1. **Das Stellenformular liegt auf `/company/jobs/new`, die Reise sucht es auf
   `/company/jobs`.** `e2e/jobs-journey.spec.ts:37-41` und
   `e2e/application-journey.spec.ts:36-39` gehen auf `/company/jobs` und füllen
   sofort „Titel". Das Formular wurde bewusst abgetrennt (eigene Adresse,
   teilbar, überlebt ein Neuladen).
   **Lösung hier: der Schnitt bleibt.** Die Liste verweist sichtbar auf „Neue
   Stelle" — als Knopf **oben in der Kopfzeile der Seite** *und* im Leerzustand,
   damit der Weg auch dann sichtbar ist, wenn es noch keine Stelle gibt. Die
   Reise muss nachgezogen werden (`docs/UEBERGABE.md` §4 nennt sie bereits als
   eine von acht veralteten); **das ist Arbeit ausserhalb dieses Territoriums.**
   Dasselbe Muster bei der Team-Einladung (`/company/team/invite`).
2. **`/company/team` kommt aus `routes/team.tsx`**, nicht aus einer Datei namens
   `company-team.tsx` — die gibt es nicht. Die Einladeseite ist eine **eigene**
   Route (`/company/team/invite`), die im Auftrag nicht auftaucht, aber von der
   Mannschaftsseite verlinkt wird und einen eigenen alten Test hat.
3. **`/careers/<slug>` kommt aus `routes/career.tsx`** (Einzahl).
4. **`GITHUB_BASE_URL` zeigt in `src/env.ts` auf Port 8010** — das ist
   notification-service; github-service läuft auf **8011** (CLAUDE.md,
   Dienstetabelle). Die Kandidatenkarte liest darüber die GitHub-Belege. `env.ts`
   ist fremdes Territorium: **nicht angefasst, hiermit gemeldet.**
5. **Die alten Seiten bekamen `principal` als Prop injiziert**
   (`principal?: MeResponse | null`), weil es keinen Sitzungs-Store gab. Neu
   kommt die Sitzung aus `state.auth`. Der dritte Zustand `unknown` existierte
   vorher nicht — die alten Tests kennen ihn nicht, die neuen müssen ihn prüfen,
   sonst zeigt jede Seite beim Kaltstart kurz „Bitte anmelden".
6. **`listOwnJobs` macht aus `403` eine leere Liste** (`{ ok: true, jobs: [] }`),
   während `/candidates` bei `403` einen eigenen Satz zeigt. Übernommen wie
   vorgefunden — aber die Seite fragt ohnehin nicht ohne aktives Unternehmen,
   also ist der Zweig unerreichbar.
7. **Die alten Tests mocken die Client-Module** (`vi.mock("../jobs/client")`).
   Neu wird stattdessen `fetch` gestubbt: damit prüft der Test auch **den
   Draht** (Pfad, Methode, snake_case im Rumpf) — genau die Ebene, an der sich
   der Draht-Fund (`19f45b6`) versteckt hatte.

---

## 6. Übersetzung

`npx tsc --noEmit` vor Beginn — **Grundrauschen, das mir nicht gehört**:

```
src/core/router/Router.tsx: Cannot find module '../../features/{auth,person,work,company,public}/routes'
```

`work/routes` und `company/routes` verschwinden mit dieser Arbeit; `auth`,
`person`, `public` gehören den anderen Strömen. Sonst ist der Baum sauber.

Prüfbefehle:

```bash
npx tsc --noEmit
npx vitest run src/features/work src/features/company
```

Nicht `pnpm build` — die anderen Bereiche fehlen noch.
