# ADR-0018: Mitgliedschaft statt Tenant-Spalte, und ein verifizierter Tenant-Wechsel

Date: 2026-08-01
Status: Accepted
Related: ADR-0017 (Tenant ist ein Unternehmensbegriff), ADR-0009 (Tenant-Kontext), ADR-0008 (Auth-Flow), [product-scope.md](../product-scope.md)

## Kontext

ADR-0017 hält fest, dass ein Tenant ein Unternehmen ist und eine natürliche
Person keinen hat. `identity-service` widersprach dem an drei Stellen
gleichzeitig:

1. `users.tenant_id` war `NOT NULL` — jede Person *musste* zu einem Unternehmen
   gehören.
2. `RegisterBody.tenant_id` und `LoginBody.tenant_id` waren Request-Body-Felder.
   `product-scope.md` verbietet den Tenant aus einem browser-kontrollierten
   Header; ein Body ist genauso client-kontrolliert, war aber nicht einmal durch
   `allow_development_tenant_header` gegated. Das Frontend verlangte deshalb vom
   Menschen, eine UUID abzutippen.
3. `uq_users_tenant_email` machte E-Mail nur pro Tenant eindeutig.

## Entscheidung

**Mitgliedschaft ist eine eigene Relation** (`user_tenant_memberships`), keine
Spalte auf `users`. Eine Person kann für mehrere Unternehmen handeln — ein
Recruiter mit mehreren Mandaten, eine Angestellte, die den Arbeitgeber wechselt,
ohne ihren Account zu verlieren. Eine Spalte könnte das nur, indem sie lügt.

**E-Mail ist global eindeutig.** Sobald der Tenant wegfällt, trägt
`uq_users_tenant_email` nicht mehr: Postgres behandelt `NULL`-Werte als
verschieden, dieselbe Adresse könnte sich unbegrenzt oft registrieren. Eine
Person ist ein Account — sie besitzt ihn, bevor ein Unternehmen es tut.

**Der aktive Tenant entsteht durch einen verifizierten Wechsel.**
`POST /auth/login` liefert ein reines Personen-Token ohne Tenant-Claim.
`POST /auth/company/{id}` prüft die Mitgliedschaft (in ADR-0019 von
`/auth/tenant/{id}` umbenannt: an der öffentlichen Grenze steht das Domänenwort) und stellt erst dann ein neues
Token-Paar mit `tenant_id` aus. Der Client *nennt* das Unternehmen, der Server
*entscheidet* — damit stammt der Tenant im Token weiterhin nie aus einer
Client-Angabe, sondern aus einer geprüften Relation. ADR-0009 und
`ClaimTenantResolver` bleiben unverändert.

Verworfen wurden: den Tenant beim Login mitzuschicken (optisch zu nah am alten
Zustand, Wechsel nur per erneutem Login) und alle Mitgliedschaften ins Token zu
legen und pro Request per Header zu wählen (bringt den Tenant zurück in einen
Header, auch wenn er gegen das Token geprüft würde).

**Der Wechsel eröffnet eine eigene Session.** Das Personen-Refresh-Token bleibt
gültig; das tenant-gebundene ist eine eigene Zeile und einzeln widerrufbar.

**Beide Ausgänge werden auditiert** — `tenant_switch` und
`tenant_switch_denied`. Die Ablehnungen sind die interessanten. Sie antwortet
`403`, nie `404`: ob ein Unternehmen existiert, darf nicht durch Probieren
herausfindbar sein.

## Konsequenzen

- Migration `0002` überführt jede bestehende `users.tenant_id` in eine
  Mitgliedschaft, bevor die Spalte fällt. Der `downgrade` ist verlustbehaftet
  und sagt das: wer mehrere Mitgliedschaften hat, hat keine richtige Antwort.
- `uq_users_email` wird angelegt, **bevor** `uq_users_tenant_email` fällt. Teilen
  sich zwei Tenants heute eine Adresse, scheitert die Migration laut — statt
  Duplikate zu behalten, die das neue Modell nicht ausdrücken kann.
- `sessions.tenant_id` und `audit_events.tenant_id` werden nullable. `NULL` heißt
  „hat als Person gehandelt" und ist die richtige Antwort, kein fehlender Wert.
- `TokenPayload.tenant_id` ist optional. Der Claim wird explizit als `null`
  geschrieben statt weggelassen, damit „handelt als Person" von einem alten
  Token ohne das Feld unterscheidbar bleibt. Betrifft auch `consent-service`,
  das dieselbe `TokenManager`-Klasse zum Verifizieren nutzt (ADR-0015).
- Das Login-Formular hat kein Mandant-Feld mehr.
- **Mitgliedschaften entstehen seit ADR-0019 beim Anlegen eines Unternehmens**
  (`POST /companies`); der Ersteller wird `admin`. Weitere Mitglieder einzuladen
  gehört weiterhin zur nächsten Scheibe. Vorhanden sind das Aggregat, der
  Repository-Port und der Lesezugriff, den der Wechsel braucht.

## Verifikation

- `tests/unit/test_commands.py` — Login vergibt kein Tenant-Token; Wechsel mit
  Mitgliedschaft gelingt; ohne Mitgliedschaft `not_a_member` **ohne** Token und
  **mit** Audit-Zeile; eine Mitgliedschaft berechtigt nicht für ein zweites
  Unternehmen; Personen- und Tenant-Session existieren nebeneinander.
- `tests/integration/test_tenant_source.py` — `/me` ist tenantlos nach Login,
  der Wechsel ohne Mitgliedschaft ist `403`, und nach gewährter Mitgliedschaft
  gewinnt der Claim gegen einen abweichenden `X-Tenant-ID`-Header.
- `apps/web/src/routes/login.test.tsx` — das Formular fragt keine Mandant-ID.
