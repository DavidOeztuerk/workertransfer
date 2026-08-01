# Registrierung & Unternehmens-Onboarding (Design)

- **Status:** Design (brainstorming-approved, pre-implementation)
- **Date:** 2026-08-01
- **Slice:** Scheiben A + B (Personen-Verifikation + Unternehmensanlage). Scheibe C
  (Einladungen, Rollen-Durchsetzung) bekommt eine eigene Spec.
- **Relates:** ADR-0017 (Tenant ist ein Unternehmen); ADR-0018 (Mitgliedschaft +
  verifizierter Wechsel); ADR-0012 (Audit sync-UoW, PII-Allowlist); ADR-0011
  (Testcontainers offline-skip); ADR-0010 (Alembic je Service);
  `docs/product-scope.md` (Consent, Discoverability, Trust-Constraints);
  ULTRAPLAN Phase 3 (DoD) und Phase 4 (`companies-service`)

## 1. Ziel & Scope

Heute kann sich niemand über die Oberfläche registrieren. `POST /auth/register`
existiert seit Phase 2, aber `apps/web` hat nur `/` und `/login` — ein Konto
entsteht ausschließlich per `curl`. Ein Unternehmen kann **überhaupt nicht**
entstehen: es gibt keine `companies`- oder `tenants`-Tabelle, und
`user_tenant_memberships.tenant_id` ist eine freie UUID ohne Fremdschlüssel.
Die Mitgliedschaft, die ADR-0018 voraussetzt, lässt sich nur per SQL-`INSERT`
anlegen.

Das blockiert die laufende Phase: die DoD von Phase 3 lautet „Profil anlegen,
Dokument hochladen, Consent erteilen/entziehen" und setzt einen Kandidaten
voraus, den es ohne Registrierungsseite nicht gibt. In keiner Phase 4–10 ist
eine Registrierungs-Route eingeplant; Phase 10 nennt nur pauschal
„Frontend ausbauen".

**In-Scope:**

- E-Mail-Versand erstmals verdrahtet (`worker-email` + Mailpit in Compose).
- Personen-Verifikation: `AccountStatus.PENDING` → `ACTIVE` per Token.
- `tenants`-Tabelle, Unternehmensanlage, Domain-Nachweis, Ersteller wird `admin`.
- Fremdschlüssel und `role` auf `user_tenant_memberships`.
- Frontend: `/register`, `/verify`, `/company/new`, Unternehmens-Umschalter.
- Umbenennung `POST /auth/tenant/{id}` → `POST /auth/company/{id}`.

**Out-of-Scope (bewusst):**

- Einladungen weiterer Mitarbeiter und Durchsetzung von `admin` vs. `member`
  (Scheibe C).
- Passwort-Reset (eigener Fluss, teilt sich später die Token-Maschinerie).
- Zweite E-Mail-Adresse pro Konto (Begründung in §5).
- Employer-Profil, Team, Kultur, Benefits, Karriereseiten — `companies-service`,
  Phase 4.
- DNS-TXT-Verifikation (zahlt auf die Karriere-Subdomain aus Phase 4 ein).
- Per-IP-Rate-Limiting an den Auth-Endpunkten (Phase 10, Marker existiert).

## 2. Architektur

Alles liegt in `apps/identity-service`. Das ist eine bewusste Entscheidung gegen
einen eigenen `companies-service` in dieser Scheibe:

`POST /auth/company/{id}` muss bei **jedem** Wechsel synchron beantworten, ob
eine Person für ein Unternehmen handeln darf. Läge die Mitgliedschaft in einem
anderen Service, wäre das ein service-übergreifender Aufruf im Hot Path — mit
Verfügbarkeitskopplung an einer Stelle, an der ein Ausfall die Anmeldung
blockiert. Die **Identität** eines Unternehmens (existiert es, welche Domain,
wer gehört dazu) ist auth-nahe Information und gehört zu identity-service.

Das **Profil** eines Unternehmens (Kultur, Benefits, Team, Stellenanzeigen,
Karriereseite) ist Domänendatum und gehört in den `companies-service` aus
Phase 4. Diese Spec legt dafür die Identität, nicht das Profil. Ein späterer
`companies-service` referenziert `tenants.id` und besitzt seine eigenen Daten;
es entsteht keine geteilte Datenbank (ADR-0004 §1).

### Der zentrale Kniff: die Domain wird abgeleitet, nie entgegengenommen

Ein Unternehmen kann nur anlegen, wer eine **bestätigte E-Mail auf dessen
Domain** besitzt. Die Domain wird serverseitig aus der Adresse des Erstellers
gelesen; der Request enthält sie nicht.

Daraus folgt dreierlei:

1. Die Domain ist bewiesen, **bevor** das Unternehmen existiert. Es gibt keinen
   unverifizierten Zwischenzustand und damit keine Statusspalte, die überall
   mitgeprüft werden müsste.
2. Die Domain kann nicht gefälscht werden, weil der Client sie nicht schickt.
   Das ist dieselbe Regel, die ADR-0018 für `tenant_id` durchgesetzt hat und die
   `product-scope.md` fordert.
3. Personen-Verifikation und Domain-Verifikation sind **ein** Mechanismus, nicht
   zwei.

### Zwei Wörter für eine Sache, mit Absicht

Domäne und HTTP-Grenze sagen **Company**, Datenbank und JWT-Claim sagen
**tenant**. Das ist keine Nachlässigkeit, sondern die günstigste Variante:
`user_tenant_memberships.tenant_id`, `audit_events.tenant_id`,
`sessions.tenant_id` und der JWT-Claim `tenant_id` tragen das Wort bereits, und
der Claim wird von consent-service über `worker_auth.TokenPayload` mitgelesen.
Eine Umbenennung wäre eine Migration über drei Tabellen plus ein Vertragsbruch
zwischen zwei Services — für ein Wort. `tenants` heißt die Tabelle deshalb
weiterhin so, ADR-0017 definiert beide Begriffe als dasselbe, und nach außen
steht das Domänenwort.

## 3. Komponenten

### 3.1 Domäne (`identity_service/domain/`)

- `user.py`: `User.register(...)` erzeugt künftig `AccountStatus.PENDING` statt
  `ACTIVE`. Neue Methode `activate()` setzt `ACTIVE` und ist idempotent-sicher
  (zweiter Aufruf wirft `AlreadyActive`). Neues Event `EmailVerified`.
- `verification.py` *(neu)*: `VerificationToken` (Wert-Objekt über dem
  Klartext-Token), `TokenPurpose` (`EMAIL_VERIFY`), Fehler `TokenExpired`,
  `TokenAlreadyUsed`, `TokenInvalid`.
- `company.py` *(neu)*: `Company` (Aggregat: `id`, `name`, `domain`),
  `EmailDomain` (Wert-Objekt, normalisiert auf Kleinschreibung, validiert),
  Fehler `PublicEmailDomain`, `DomainAlreadyClaimed`, `AccountNotConfirmed`.
- `membership.py`: `TenantMembership` bekommt `role: MembershipRole`
  (`ADMIN` | `MEMBER`).
- `audit.py`: neue Aktionen `EMAIL_VERIFIED`, `COMPANY_CREATED`.

Die Freemail-Sperrliste ist eine `frozenset`-Konstante in `company.py`
(`gmail.com`, `googlemail.com`, `gmx.de`, `gmx.net`, `web.de`, `outlook.com`,
`hotmail.com`, `yahoo.com`, `yahoo.de`, `icloud.com`, `me.com`, `proton.me`,
`protonmail.com`, `t-online.de`, `freenet.de`, `aol.com`, `mail.com`,
`zoho.com`, `yandex.com`, `gmx.at`, `gmx.ch`). Sie ist bewusst kurz und
erweiterbar; Vollständigkeit ist nicht erreichbar und auch nicht nötig — sie
verhindert die offensichtlichen Fälle.

### 3.2 Application (`identity_service/application/`)

Neue Commands + Handler, alle im Muster der bestehenden (`Result`, UoW vom
Router getrieben):

- `RegisterUserCommand` — erweitert um Token-Erzeugung und Mail-Auftrag.
- `VerifyEmailCommand { token }` → `Result[None]`.
- `ResendVerificationCommand { email }` → `Result[None]`. Erfolgreich auch dann,
  wenn nichts zu senden war (§4.3).
- `CreateCompanyCommand { user_id, name }` → `Result[Company]`.
- `ListMembershipsQuery { user_id }` → `Result[list[MembershipView]]`.
  `MembershipView` ist der Domänen-Lesetyp; der Router bildet ihn auf das
  Vertrags-DTO `MembershipV1` ab (kein Domänentyp an der Grenze, ADR-0004 §1).

Neue Ports in `ports.py`:

- `VerificationTokenRepository` — `add`, `get_by_hash`, `consume`.
- `CompanyRepository` — `add`, `get_by_domain`, `get_by_id`.
- `MembershipRepository` — erweitert um `add`, `list_for_user_detailed`.
- `Mailer` — `send(to, subject, body)`. Die Application kennt kein SMTP.

### 3.3 Infrastruktur

- `infrastructure/mail.py` *(neu)*: `SmtpMailer` implementiert den `Mailer`-Port
  über `worker_email.SMTPBackend`. Ein `NullMailer` (loggt statt zu senden) für
  Tests und für lokale Läufe ohne Mailcatcher.
- `infrastructure/tokens.py` *(neu)*: `secrets.token_urlsafe(32)` erzeugt den
  Klartext, `hashlib.sha256` den gespeicherten Wert. Der Klartext verlässt den
  Prozess nur in der Mail.
- Repositories für `tenants` und `email_verification_tokens`.

### 3.4 Präsentation

| Methode | Pfad | Auth | Zweck |
|---|---|---|---|
| POST | `/auth/register` | — | Konto anlegen (`PENDING`), Mail versenden |
| POST | `/auth/verify-email` | — | Token einlösen → `ACTIVE` |
| POST | `/auth/resend-verification` | — | Bestätigungsmail erneut senden |
| POST | `/companies` | Bearer/Cookie | Unternehmen anlegen, Ersteller wird `admin` |
| GET | `/me/companies` | Bearer/Cookie | Eigene Mitgliedschaften |
| POST | `/auth/company/{id}` | Bearer/Cookie | Wechsel (bisher `/auth/tenant/{id}`) |

Alle Bodies sind versionierte `worker-contracts`-DTOs (ADR-0004 §1):
`RegisterUserV1`, `VerifyEmailV1`, `ResendVerificationV1`, `CreateCompanyV1`,
`CompanyV1`, `MembershipV1`.

### 3.5 Frontend (`apps/web`)

- `/register` — E-Mail, Passwort, Anzeigename. Danach ein Hinweis „Wir haben dir
  eine E-Mail geschickt" mit Schaltfläche „erneut senden".
- `/verify?token=…` — löst beim Laden ein, zeigt Erfolg oder Ablauf mit
  „erneut senden", leitet bei Erfolg nach `/login`.
- `/company/new` — Name des Unternehmens; die Domain wird angezeigt, aber nicht
  eingegeben („Wird als **firma.de** angelegt"). Der Einstieg erscheint nur,
  wenn die Domain des Kontos keine Freemail-Domain ist.
- Navigation: Umschalter aus `GET /me/companies`; „als Person" ist der
  Standardzustand.

## 4. Datenfluss & Schema

### 4.1 Migration `0003_verification_and_companies`

```
tenants                          (neu)
  id           uuid   pk
  name         text   not null
  domain       citext not null unique      -- der Nachweis selbst
  created_at   timestamptz not null

email_verification_tokens        (neu)
  id           uuid   pk
  user_id      uuid   not null  fk users(id) on delete cascade
  token_hash   char(64) not null unique     -- sha256 hex, nie Klartext
  purpose      text   not null              -- 'email_verify'
  expires_at   timestamptz not null
  consumed_at  timestamptz null
  created_at   timestamptz not null
  index (user_id, purpose)

user_tenant_memberships          (geändert)
  + role       text not null default 'member'
  + fk tenant_id -> tenants(id) on delete cascade
```

Der Fremdschlüssel ist der Punkt, an dem die Migration scheitern kann: bestehende
Mitgliedschaften zeigen auf UUIDs ohne `tenants`-Zeile (in Entwicklung per Hand
eingefügt). Die Migration legt deshalb **vor** dem Constraint für jede
verwaiste `tenant_id` eine Platzhalter-Zeile in `tenants` an
(`name = 'Unbekannt (migriert)'`, `domain = '<uuid>.invalid'`). `.invalid` ist
per RFC 2606 garantiert nicht auflösbar und kann nie mit einer echten Domain
kollidieren. Nichts wird stillschweigend gelöscht.

Der `downgrade` entfernt Constraint, Spalte und beide Tabellen; die
Platzhalter-Zeilen verschwinden mit `tenants`.

### 4.2 Registrierung

```
POST /auth/register {email, password, display_name}
  ├─ UoW: User(PENDING) + VerificationToken(hash, +24h) + Audit(REGISTER)
  ├─ commit
  └─ danach, außerhalb der Transaktion: Mail mit Klartext-Token
  → 201 {"status": "registered"}
```

**Existiert die Adresse bereits**, entsteht kein Konto. Die Antwort ist
trotzdem `201` mit identischem Body, und stattdessen geht eine Mail an den
bestehenden Besitzer („jemand hat versucht, sich mit deiner Adresse zu
registrieren"). Begründung: `product-scope.md` gibt der Person die Kontrolle
darüber, ob sie auffindbar ist. Eine `409`-Antwort würde „ist diese Person
hier?" beantworten, **ohne** den Consent-Ledger zu fragen — auf einem
Transfermarkt genau die Information, die jemanden den Arbeitsplatz kosten kann.
Die Oberfläche sagt in beiden Fällen dasselbe, und das ist in beiden Fällen wahr.

### 4.3 Bestätigung

```
POST /auth/verify-email {token}
  ├─ sha256(token) → Zeile suchen
  ├─ consumed_at gesetzt?  → 400 token_invalid
  ├─ expires_at < now?     → 410 token_expired
  └─ UoW: user.activate() + token.consume() + Audit(EMAIL_VERIFIED)
  → 200
```

Abgelaufen (`410`) und ungültig (`400`) werden unterschieden, weil der Token nur
dem Empfänger der Mail bekannt ist — die Unterscheidung verrät nichts und
erlaubt der Oberfläche, gezielt „erneut senden" anzubieten.

**Erneut senden** (`POST /auth/resend-verification`) antwortet **immer** `202`,
unabhängig davon, ob die Adresse existiert oder das Konto längst `ACTIVE` ist —
sonst wäre der Endpunkt der Enumerationskanal, den §4.2 gerade geschlossen hat.
Versendet wird nur bei einem existierenden `PENDING`-Konto. Dabei werden alle
offenen Tokens dieses Kontos und Zwecks als verbraucht markiert, bevor ein neues
ausgestellt wird: sonst blieben beliebig viele gültige Links gleichzeitig in
Umlauf, und der älteste — womöglich in einem fremden Postfach gelandete —
funktionierte weiter.

### 4.4 Login mit unbestätigtem Konto

Heute wirft `assert_can_log_in()` bei jedem Nicht-`ACTIVE`-Status
`AccountDisabled`, was der Router pauschal auf `401 invalid credentials`
abbildet — eine Sackgasse für jemanden, der die Mail übersehen hat.

Neu: bei **korrektem** Passwort und `PENDING` antwortet der Router
`403 email_not_confirmed`. Das verrät nichts, was das korrekte Passwort nicht
ohnehin beweist. Bei falschem Passwort bleibt es beim generischen `401`, und
`SUSPENDED`/`DISABLED` bleiben ebenfalls beim generischen `401`.

### 4.5 Unternehmensanlage

```
POST /companies {name}                        (authentifiziert)
  ├─ Konto ACTIVE?                → 403 account_not_confirmed
  ├─ domain = user.email.domain
  ├─ domain in FREEMAIL?          → 422 public_email_domain
  ├─ domain bereits vergeben?     → 409 domain_already_claimed
  └─ UoW: Company + Membership(role=admin) + Audit(COMPANY_CREATED, tenant_id)
  → 201 {id, name, domain}
```

Die Audit-Zeile trägt hier erstmals eine `tenant_id`, weil die Handlung ein
Unternehmen betrifft — konsistent mit ADR-0017, wonach persönliche Handlungen
`NULL` tragen.

## 5. Fehlerbehandlung

**Mailversand ist nicht Teil der Transaktion.** Konto und Token committen, der
Versand passiert danach. Schlägt er fehl, wird auf `error` geloggt und trotzdem
`201` geantwortet. Ein `500` würde offen lassen, ob das Konto existiert — es
existiert. Der Reparaturweg ist „erneut senden", und er funktioniert. Eine Mail
ist kein Audit-Ereignis und gehört ausdrücklich nicht in die UoW, die ADR-0012
schützt.

**Doppelte Bestätigung.** `activate()` auf einem bereits aktiven Konto wirft
`AlreadyActive`; der Router bildet das auf `200` ab, nicht auf einen Fehler. Wer
zweimal auf denselben Link klickt, hat nichts falsch gemacht. Der Token selbst
ist dennoch einmalig (`consumed_at`), sodass ein abgefangener Link nach der
ersten Nutzung wertlos ist.

**Private Adresse, Unternehmen gewünscht.** Wer sich mit `gmail.com` registriert
hat, kann kein Unternehmen anlegen und bekommt `422`. Die Oberfläche zeigt den
Einstieg dann gar nicht erst. Eine zweite, geschäftliche Adresse pro Konto wäre
die flexible Lösung, verlangt aber ein eigenes Adress-Aggregat mit eigener
Verifikation und eigener Primär-Logik — bewusst später. Der Weg heute:
mit der Arbeitsadresse registrieren.

**Registrierung ist für private Adressen uneingeschränkt offen.** Sie sind der
Normalfall: der Wechselwillige und der Arbeitssuchende brauchen kein
Unternehmen. Die Sperrliste greift an genau einer Stelle — beim Beanspruchen
einer Domain — und nirgends sonst.

## 6. Tests

**Unit (ohne Docker):**

- Token: Hash-Erzeugung, Ablauf, Einmalverbrauch, unbekannter Token.
- `EmailDomain`: Normalisierung, Ableitung aus der Adresse, ungültige Eingaben.
- Freemail-Sperrliste: Treffer und Nicht-Treffer, Groß-/Kleinschreibung.
- `User.register` erzeugt `PENDING`; `activate()` setzt `ACTIVE`; zweiter Aufruf
  wirft.
- `handle_create_company`: Happy Path, `PENDING`-Ablehnung, Freemail-Ablehnung,
  bereits vergebene Domain; Ersteller erhält `admin`; Audit trägt `tenant_id`.
- `handle_register`: Konto existiert bereits → kein zweites Konto, trotzdem
  Erfolg, und der Mailer wurde mit der Warn-Nachricht aufgerufen.
- `handle_resend`: unbekannte Adresse → Erfolg ohne Mail; bereits `ACTIVE` →
  Erfolg ohne Mail; offenes Token wird beim erneuten Senden entwertet.
- Login mit `PENDING` und korrektem Passwort → `email_not_confirmed`;
  Login mit falschem Passwort auf `PENDING` → generisches `401`.

**Integration (Testcontainers, ADR-0011 offline-skip):**

- Migration `0003` hoch und runter; verwaiste `tenant_id` erhält eine
  Platzhalter-Zeile statt gelöscht zu werden.
- Voller Weg **ohne einen einzigen SQL-`INSERT`**: registrieren → Mail in
  Mailpit abholen → Token einlösen → anmelden → Unternehmen anlegen →
  `POST /auth/company/{id}` → `/me` zeigt die `tenant_id`. Genau dieser Test
  schließt die Lücke, die den Smoke-Test bisher zu einem Hand-`INSERT` zwang.
- `worker-email` gegen Mailpit: der SMTP-Pfad ist implementiert, aber **nie
  gelaufen** — hier ist mit Überraschungen zu rechnen.

**Frontend (Vitest):**

- Registrierungsformular sendet, zeigt den Bestätigungshinweis, erzwingt keine
  Mandanten-Eingabe.
- Verify-Seite: Erfolg, abgelaufen mit „erneut senden".
- `/company/new` erscheint bei `firma.de`, nicht bei `gmail.com`.
- Umschalter listet die Mitgliedschaften aus `GET /me/companies`.

## 7. ADR

Eine neue ADR: **ADR-0019 — Verifikation über die E-Mail-Domain, Unternehmen
entstehen bewiesen.** Sie hält fest, warum die Domain abgeleitet statt
entgegengenommen wird, warum es keinen unverifizierten Unternehmenszustand gibt,
warum Personen- und Domain-Verifikation derselbe Mechanismus sind, und warum
`POST /auth/register` bei bekannter Adresse `201` antwortet statt `409`.

ADR-0018 wird um einen Satz ergänzt: Mitgliedschaften entstehen jetzt beim
Anlegen eines Unternehmens; das Anlegen weiterer Mitglieder bleibt Scheibe C.

## 8. Definition of Done

- Eine Person registriert sich im Browser mit einer **privaten** Adresse,
  bestätigt sie über die Mail in Mailpit, meldet sich an und sieht `/me` mit
  `tenant_id: null`.
- Eine Person mit Arbeitsadresse legt zusätzlich ein Unternehmen an, wechselt
  hinein und sieht `/me` mit gesetzter `tenant_id` — **ohne Datenbankzugriff von
  Hand**.
- Eine zweite Person kann dieselbe Domain nicht beanspruchen (`409`).
- `docker compose up --build` bringt Mailpit mit hoch; die Oberfläche unter
  `:8025` zeigt die Mails.
- `make check` grün, inklusive der neuen Integrationstests mit Docker.
- ADR-0019 geschrieben, `CLAUDE.md` und `docs/ROADMAP.md` nachgezogen.
