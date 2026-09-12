# E2.6 — Registrierung: Person oder Unternehmen

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans.

**Goal:** Bei der Registrierung wird gewählt, ob ein Mensch oder ein Unternehmen
entsteht. Das Unternehmen wird **bei der Bestätigung der Adresse** angelegt, in
derselben Transaktion. Wer als Person registriert ist, findet in der Anwendung
keinen Weg mehr, ein Unternehmen anzulegen.

**Zweig:** eigener Zweig auf `ui-drei-referenzrouten`.

## Warum diese Reihenfolge die einzige ist

Gemessen in `identity-service`:

- `POST /companies` liegt **auf identity-service selbst**
  (`presentation/http/company_router.py`) — **kein Dienst-zu-Dienst-Aufruf**,
  gleiche Datenbank, gleiche Transaktion.
- `handle_create_company` verlangt `user.status is AccountStatus.ACTIVE` und
  wirft sonst `AccountNotConfirmed`. Die Domain wird aus der **bestätigten**
  Adresse abgeleitet und steht nicht im Request — das ist die
  Fälschungssicherheit aus ADR-0019.
- `Company.create` lehnt Freemail-Domains selbst ab (`PublicEmailDomain`), und
  `handle_create_company` prüft `DomainAlreadyClaimed`.
- In `handle_verify_email` steht `user.activate(now=now)` **vor** dem `save()`.
  Danach ist der Nutzer `ACTIVE` — die Unternehmensanlage passt also direkt
  dahinter, ohne neue Domänenlogik.

Daraus folgt zwingend: **Wahl bei der Registrierung, Anlage bei der
Bestätigung.** Ein Unternehmen bei `POST /auth/register` anzulegen ist
unmöglich, weil das Konto dort `PENDING` ist.

## Wo die Absicht liegt: auf dem Server

Ein `localStorage`-Merker (wie `apps/web/src/jobs/intent.ts` es für eine Stelle
tut) wäre billiger und **falsch**: Bestätigungsmails werden oft auf einem
anderen Gerät geöffnet. Dort ist der Merker weg, und aus einer
Unternehmensregistrierung würde stillschweigend eine Personenregistrierung —
ohne dass es jemand merkt.

Gespeichert wird **eine** nullable Spalte `users.pending_company_name`, nicht
zwei. Ein separates `account_type` wäre ableitbar und könnte deshalb
widersprüchlich werden; `NULL` heißt Person, ein Name heißt Unternehmen. Nach
der Anlage wird die Spalte geleert — sie heißt „pending".

## Zwei Prüfungen an zwei Stellen, und das ist keine Willkür

| Prüfung | Wo | Warum dort |
|---|---|---|
| **Freemail** | bei der Registrierung | Ob eine Adresse privat ist, steckt in der Adresse, die die Person gerade selbst getippt hat. Verrät nichts. |
| **Domain schon beansprucht** | erst bei der Bestätigung | Bei der Registrierung wäre die Antwort ein Enumerationskanal: *„ist firma.de schon auf der Plattform?"*, beantwortbar von jedem, der die Domain errät. Nach der Bestätigung fragt jemand mit **nachgewiesener** Adresse auf dieser Domain — ein Kollege dessen, der sie beansprucht hat. Dem darf man es sagen. |

Daraus entsteht ein Zustand, den es heute nicht gibt: **Konto bestätigt,
Unternehmen abgelehnt.** Die Person ist dann angemeldet und nutzbar, nur ohne
Unternehmen. `/verify` muss das sagen können, sonst ist die Bestätigungsseite
grün, während die Hälfte der Absicht verpufft ist.

## Aufgabe 1 — Vertrag, Entität, Tabelle, Migration

**Files:**
- Modify `packages/worker-contracts/src/worker_contracts/identity.py`
- Modify `apps/identity-service/src/identity_service/domain/user.py`
- Modify `apps/identity-service/src/identity_service/infrastructure/database/models.py`
- Modify `apps/identity-service/src/identity_service/infrastructure/database/repositories.py`
- Create `apps/identity-service/migrations/versions/0008_pending_company_intent.py`

1. `RegisterUserV1` bekommt `company_name: str | None = Field(default=None, max_length=200)`.
   **Optional, nicht Pflicht** — der Vertrag ist versioniert (V1), und ein
   Pflichtfeld wäre ein Bruch. „Person" ist ohnehin der richtige Standard
   (ADR-0017: registrieren ist der Akt einer Person).
2. `User` bekommt `pending_company_name: str | None = None` (nach `_events`,
   weil `slots=True` Felder mit Standardwert hinten verlangt), und
   `User.register(..., pending_company_name: str | None = None)`.
3. `UserModel.pending_company_name`: `String(200)`, `nullable=True`.
4. `_to_domain` in `repositories.py` trägt das Feld mit — **und die Stelle, die
   `UserModel` schreibt.** Beides prüfen: das Aggregat kommt losgelöst zurück,
   ein vergessenes `save()` kostet im Test nichts und verliert die Absicht in
   Produktion.
5. Migration `0008`, `down_revision = "0007"`.

**Beweis:** ein Integrationstest (Testcontainers, `ADR-0011`-Selbstübersprung)
registriert mit Firmennamen und liest den Nutzer zurück — der Name ist da.

## Aufgabe 2 — Registrierung nimmt die Absicht an

**Files:** `apps/identity-service/src/identity_service/application/commands.py`,
`presentation/http/router.py`

1. `RegisterUserCommand.company_name: str | None = None`.
2. `handle_register`: **Freemail-Prüfung ZUERST**, vor der Existenzprüfung.
   Sonst antwortet der Endpunkt bei bekannter Adresse anders als bei
   unbekannter, und die Zusage „dieselbe Antwort" fällt. Bei Freemail +
   Firmenname → `PublicEmailDomain` → HTTP 422.
3. `User.register(..., pending_company_name=cmd.company_name)`.
4. Router: `RegisterUserCommand(..., company_name=body.company_name)`.

**Beweis, drei Tests:** mit Firmenname wird die Absicht gespeichert; mit
Freemail + Firmenname kommt 422; **ohne** Firmenname bleibt alles wie heute
(der Regressionstest, der die Rückwärtskompatibilität des Vertrags hält).

## Aufgabe 3 — Bestätigung legt das Unternehmen an

**Files:** `commands.py`, `presentation/http/router.py`

1. In `handle_verify_email`, **nach** `user.activate()` + `save()` + Audit:
   ist `user.pending_company_name` gesetzt, `handle_create_company(
   CreateCompanyCommand(user_id=user.id.value, name=...), deps=deps, repos=repos)`
   aufrufen. Danach `pending_company_name = None` und **erneut speichern**.
2. Der Rückgabewert von `handle_verify_email` muss den Ausgang tragen. Heute
   `Result[None]`; neu `Result[str | None]` mit dem Firmennamen, oder ein kleines
   Ergebnisobjekt mit `company_name` und `company_error`.
3. Der Endpunkt antwortet heute `dict[str, str]` — **untypisiert**, also kosten
   zusätzliche Schlüssel keinen Vertragsbruch: `{"status": "ok", "company":
   "..."} ` bzw. `{"status": "ok", "company_error": "domain_claimed"}`.
4. **Die Bestätigung selbst darf nicht scheitern, wenn das Unternehmen
   scheitert.** Das Konto ist bestätigt; das ist unumkehrbar und richtig. Ein
   Rollback würde jemanden aussperren, weil ein Firmenname vergeben war.

**Beweis, vier Tests:** Absicht gesetzt → Unternehmen existiert, Ersteller ist
`admin`, Spalte geleert; Domain schon beansprucht → Konto **trotzdem** aktiv und
`company_error` in der Antwort; keine Absicht → kein Unternehmen; zweiter Klick
auf denselben Link legt **kein zweites** Unternehmen an (die Spalte ist geleert —
das ist die Idempotenz).

## Aufgabe 4 — Die Registrierungsseite

**Files:** `apps/web/src/routes/register.tsx`, `register.test.tsx`,
`apps/web/src/auth/client.ts`, `apps/web/src/routes/home.tsx`

1. `RadioGroup` (aus E1) mit zwei Optionen: „Ich bin auf Jobsuche" /
   „Ich registriere ein Unternehmen". Jede Option trägt ihren Satz — drei Wörter
   sind keine drei Entscheidungen.
2. Bei „Unternehmen": ein `Field` „Name des Unternehmens", und **sofort** ein
   `Alert`, wenn die getippte Adresse eine Freemail-Domain ist
   (`isPublicEmailDomain` existiert in `auth/client.ts` — sie wird nach dem
   Löschen von `company-new.tsx` sonst verwaist). Die Absage spricht weiterhin
   der Server aus (422); die Oberfläche steuert nur die Sichtbarkeit.
3. `RegisterInput` bekommt `companyName?: string`, der Client sendet
   `company_name`.
4. `home.tsx`: die zwei Hero-Knöpfe tragen die Absicht — `/register` und
   `/register?as=company`; `register.tsx` liest den Parameter als Vorauswahl.

## Aufgabe 5 — `/verify` sagt, was aus dem Unternehmen wurde

**Files:** `apps/web/src/routes/verify.tsx`, `verify.test.tsx`

Drei Ausgänge statt zwei: bestätigt · bestätigt **ohne** Unternehmen (mit Grund)
· fehlgeschlagen. Der zweite ist der neue, und ohne ihn ist die Seite grün,
während die halbe Absicht verpufft ist.

**Falle aus `stack.ts`:** die E2E-Hilfe prüft die Überschrift
**buchstabengetreu** (`"E-Mail bestätigt", exact: true`), weil ein weiches
`/bestätigt/i` auch „Wird bestätigt…" trifft. Eine neue Überschrift für den
dritten Ausgang darf diese Prüfung nicht mehrdeutig machen.

## Aufgabe 6 — `/company/new` verschwindet, und alles was daran hängt

Gemessen hängen daran **mehr Stellen als die Seite**:

| Was | Anzahl |
|---|---|
| Route, Seite, Tests | `company-new.tsx` (101), `company-new.test.tsx` (33 Zeilen), Router-Eintrag, Menüeintrag `app.tsx` |
| verwaister Client | `createCompany` + `CreateCompanyResult` in `auth/client.ts` |
| Links **in der Anwendung** | **5**: candidates, company-jobs, company-profile, company-transfers, team — je *„Wähle oben ein Unternehmen — oder lege eines an"* |
| **E2E-Reisen** | **18 Stellen in 10 Reisen** |

1. Die fünf Sätze müssen wahr werden: ohne Weg zum Anlegen heißt es sinngemäß
   *„oder lass dich von einem Kollegen einladen"* (`/invitation` existiert).
2. `POST /companies` **bleibt** auf dem Server: die Bestätigung braucht dieselbe
   Domänen-Operation, und ein unverlinkter Endpunkt ist kein Irrweg für einen
   Menschen — eine unverlinkte Seite schon.
3. `stack.ts`: `registerAndConfirm(page, email, displayName, companyName?)` legt
   das Unternehmen über die Registrierung an. Die zehn Reisen **schrumpfen**
   dabei: aus sechs Zeilen (navigieren, tippen, absenden, prüfen) wird ein
   Parameter.

**Beweis:** `make validate-e2e` — und die Zahl der gelaufenen Reisen muss
**gleich bleiben**. Ein Umbau, der eine Reise verliert, sieht in einem grünen
Lauf genauso aus wie einer, der keine verliert.

## Gate für den ganzen Schnitt

`make check` (Python **und** Frontend), `pnpm build`, `make validate-e2e` mit
unveränderter Reisezahl, plus der Screenshot-Vergleich von `/register` und
`/verify` — dort ändert sich die Oberfläche **absichtlich**, und das ist genau
der Fall, für den die zwei Listen im PR-Text da sind.

## Was dieser Schnitt NICHT enthält

- **Rollen erzwingen.** `admin` gegen `member` wird nirgends geprüft, und keine
  Route prüft `tenant_id` — die Kopfzeile *versteckt* nur. Das ist ein eigener
  Backend-Schnitt und Voraussetzung für `/company/admin`.
- **Agenten-Übersichten.** Berater existiert nicht; Scout zielt auf Menschen und
  ist damit der Agent, den ADR-0022 ohne eigene Abwägung ausschließt.
- **Der Stand-Bereich auf `/overview`** — festgelegt in der Routenkarte, gebaut
  in E3e.
