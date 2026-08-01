# ADR-0019: Verifikation über die E-Mail-Domain — Unternehmen entstehen bewiesen

Date: 2026-08-01
Status: Accepted
Related: ADR-0017 (Tenant ist ein Unternehmensbegriff), ADR-0018 (Mitgliedschaft + verifizierter Wechsel), ADR-0012 (Audit sync-UoW), [product-scope.md](../product-scope.md), [Design-Spec](../superpowers/specs/2026-08-01-registration-and-company-onboarding-design.md)

## Kontext

ADR-0017 und ADR-0018 haben Tenant und Mitgliedschaft geklärt, aber niemand
konnte ein Unternehmen anlegen: es gab keine `tenants`-Tabelle, und
`user_tenant_memberships.tenant_id` war eine freie UUID ohne Fremdschlüssel. Die
Mitgliedschaft, die der Tenant-Wechsel voraussetzt, entstand nur per
SQL-`INSERT` von Hand.

Gleichzeitig konnte sich niemand über die Oberfläche registrieren — `apps/web`
hatte nur `/` und `/login`. Damit war die laufende Phase 3 sich selbst etwas
schuldig: ihre Definition of Done verlangt „Profil anlegen, Dokument hochladen,
Consent erteilen" und setzt einen Kandidaten voraus, den es ohne
Registrierungsseite nicht gab.

Die offene Frage war, was eine Unternehmensverifikation eigentlich beweisen
soll — und wie sich das bauen lässt, ohne einen zweiten Verifikationsapparat
neben der Personen-Bestätigung zu betreiben.

## Entscheidung

**Die Firmendomain wird abgeleitet, nie entgegengenommen.** Ein Unternehmen kann
nur anlegen, wer eine **bestätigte E-Mail auf dessen Domain** besitzt; der
Server liest die Domain aus der Adresse des Erstellers, der Request enthält sie
nicht. `CreateCompanyV1` hat deshalb genau ein Feld: den Namen.

Daraus folgt dreierlei:

1. Die Domain ist bewiesen, **bevor** das Unternehmen existiert. Es gibt keinen
   unverifizierten Zwischenzustand und damit keine Statusspalte, die jeder
   spätere Lesepfad mitprüfen müsste.
2. Sie kann nicht gefälscht werden, weil der Client sie nicht schickt —
   dieselbe Regel, die ADR-0018 für `tenant_id` durchgesetzt hat und die
   `product-scope.md` fordert.
3. Personen- und Domain-Verifikation sind **ein** Mechanismus, nicht zwei.

**Registrierung bleibt für private Adressen uneingeschränkt offen.** Sie sind
der Normalfall: der Wechselwillige und der Arbeitssuchende brauchen kein
Unternehmen und bekommen keins. Die Freemail-Sperrliste greift an genau einer
Stelle — beim Beanspruchen einer Domain — und nirgends sonst. Ohne sie könnte
sich jemand `gmail.com` als Unternehmen sichern.

**`POST /auth/register` antwortet auch bei bekannter Adresse `201`.** Es
entsteht kein zweites Konto; stattdessen geht eine Warnmail an den echten
Besitzer. Das bisherige `409` beantwortete „ist diese Person hier?" **ohne den
Consent-Ledger zu fragen** — auf einem Transfermarkt genau die Information, die
jemanden den Arbeitsplatz kosten kann. `product-scope.md` gibt die
Auffindbarkeit der Person, nicht dem Anfragenden. `POST /auth/resend-verification`
antwortet aus demselben Grund immer `202`.

**Ein unbestätigtes Konto bekommt `403`, kein `401`.** Bei korrektem Passwort
verrät das nichts, was das Passwort nicht ohnehin beweist — und ohne diesen Fall
wäre ein Konto, dessen Bestätigungsmail im Spam liegt, eine Sackgasse.
Gesperrte Konten bleiben beim generischen `401`.

**Der Mailversand liegt außerhalb der Transaktion.** Konto und Token committen;
scheitert der Versand, wird geloggt und trotzdem `201` geantwortet. Ein `500`
würde offen lassen, ob das Konto existiert — es existiert. Der Reparaturweg ist
„erneut senden". Eine Mail ist kein Audit-Ereignis und gehört nicht in die UoW,
die ADR-0012 schützt.

**`worker-email` wird nicht verwendet.** Sein `SMTPBackend.send()` ruft
unbedingt `server.login()` auf — Mailpit und die meisten Entwicklungs-Catcher
kennen kein AUTH, der Aufruf scheitert also zwangsläufig. Es schluckt zudem jede
Exception zu einem nackten `False` und zöge `boto3`, `sendgrid` und `aiohttp` in
ein Image, das eine Textmail verschicken soll. Stattdessen ein schlanker
`SmtpMailer` auf `smtplib`; der `Mailer`-Port bleibt in der Application-Schicht.
`worker-email` bleibt unangetastet für einen späteren notification-service.

## Konsequenzen

- Wer sich mit einer privaten Adresse registriert hat, kann kein Unternehmen
  anlegen und muss sich dafür mit der Arbeitsadresse registrieren. Eine zweite,
  geschäftliche Adresse pro Konto wäre die flexible Lösung, verlangt aber ein
  eigenes Adress-Aggregat mit eigener Verifikation — bewusst später.
- Tokens werden **nur als SHA-256-Hash** gespeichert. Eine geleakte
  Datenbankzeile darf keine Kontoübernahme sein.
- Erneutes Senden entwertet offene Tokens, bevor es ein neues ausstellt; sonst
  blieben beliebig viele gültige Links in Umlauf und der älteste — womöglich
  fehlgeleitete — funktionierte weiter.
- Migration `0003` legt für jede verwaiste `tenant_id` eine Platzhalter-Zeile an
  (`<uuid>.invalid`, per RFC 2606 nie auflösbar), bevor der Fremdschlüssel
  entsteht. Nichts wird stillschweigend gelöscht.
- **Aggregate kommen losgelöst aus dem Repository.** `_to_domain` baut ein neues
  Objekt; eine Mutation erreicht die Datenbank nur über `UserRepository.save()`.
  Das ist beim Bauen teuer aufgefallen: `verify-email` meldete `200`, während
  die Freischaltung nur im Arbeitsspeicher stand — sichtbar erst gegen den
  laufenden Stack, weil die Test-Fakes dieselbe Instanz zurückgeben.
- Mitgliedschaften entstehen jetzt beim Anlegen eines Unternehmens. Weitere
  Mitglieder einzuladen bleibt der nächsten Scheibe; `admin` vs. `member` wird
  noch nirgends durchgesetzt.

## Verifikation

- `apps/identity-service/tests/integration/test_registration_flow.py` — der
  ganze Weg ohne einen einzigen SQL-`INSERT`: registrieren, Token aus der
  versandten Mail, bestätigen, anmelden, Unternehmen anlegen, hineinwechseln.
  Dazu die Regression zur Persistenz, private Adressen, doppelte Domain und der
  fehlende Enumerationskanal.
- `tests/unit/test_commands.py` — Token-Lebenszyklus, Freemail-Ablehnung,
  Domain-Ableitung, Warnmail bei bekannter Adresse, Mailversand-Ausfall.
- `apps/web/src/routes/register.test.tsx`, `company-new.test.tsx` — kein
  Mandantenfeld, identischer Hinweis bei bekannter Adresse, kein Domain-Feld.
