# Übergangsschulden Python → .NET

Hier steht, was **nur** existiert, weil beide Systeme eine Zeit lang
nebeneinander laufen — und was daraus wird, wenn sie es nicht mehr tun.

Der Ordner `bugs/` ist für Fehler, die *Girder* gehören. Was hier steht, gehört
**uns**: bewusst getroffene Übergangsentscheidungen, jede einzelne richtig für
den Übergang und falsch danach. Ohne diese Liste bleiben sie stehen, weil eine
Zeile, die funktioniert, niemanden mehr stört.

Jede Stelle im Code trägt einen Verweis hierher.

---

## Ü-1 · `worker_auth` ignoriert die Zielgruppe

**Wo:** `packages/worker-auth/src/worker_auth/jwt.py`, `TokenManager.verify_token`

**Jetzt:** `pyjwt.decode(..., options={"verify_aud": False})`

**Warum:** Girder schreibt in jeden Token ein `aud` und kann es nicht lassen
(`bugs/jwtservice-kann-nicht-ohne-aud-ausstellen.md`). PyJWT lehnt einen Token
mit `aud` ab, wenn keine Zielgruppe erwartet wird — Python hätte sonst keinen
einzigen .NET-Token gelesen.

**Was daraus wird:** `pyjwt.decode(..., audience=<die Zielgruppe>)`. Dann prüft
Python die Zielgruppe, statt sie zu ignorieren. Das ist strenger als der Zustand
vor dem Übergang, in dem es gar keine Zielgruppe gab.

**Woran man merkt, dass es Zeit ist:** kein Token mehr umläuft, der vor der
Umstellung ausgestellt wurde — also spätestens eine Erneuerungsfrist (24 h) nach
dem Umstieg auf den .NET-identity-service. Bis dahin gibt es Token *ohne* `aud`,
und `audience=…` würde sie mit `MissingRequiredClaimError` abweisen.

**Kosten des Vergessens:** Python nimmt Token an, die für einen anderen
Empfänger ausgestellt wurden. Solange es nur einen Aussteller und eine
Zielgruppe gibt, folgenlos — beim zweiten nicht mehr.

---

## Ü-2 · Der .NET-Dienst nimmt Token ohne `iss`/`aud` an

**Wo:** Composition Root von `WorkerTransfer.Identity.Api`

**Jetzt:** `AudienceValidator` und `IssuerValidator` als Delegat — fehlt der
Anspruch, wird angenommen; steht er drin, muss er unserer sein.

**Warum:** Die Token, die der Python-identity-service ausgestellt hat, tragen
weder `iss` noch `aud`; `KeyRing.ValidationParameters` verlangt beides fest. Ein
Browser, der beim Umstieg ein gültiges Zugriffs-Cookie hält, soll nicht mitten
im Formular abgewiesen werden.

**Was daraus wird:** beide Delegaten fallen weg. `ValidateIssuer` und
`ValidateAudience` stehen dann wieder allein.

**Woran man merkt, dass es Zeit ist:** dasselbe Signal wie Ü-1.

**Kosten des Vergessens:** ein Token ohne Aussteller wird angenommen. Das ist
genau die Prüfung, die den Wechsel von einem geteilten Geheimnis zu getrennten
Schlüsseln erst trägt.

---

## Ü-3 · Zwei Ansprüche im Token, die nur Python braucht

**Wo:** die Tokenausgabe in `WorkerTransfer.Identity.Infrastructure`

**Jetzt:** `CustomClaims` trägt `tenant_id` (neben Girders `tenant`) und
`type` (`"access"`).

**Warum:** Pythons `TokenPayload` verlangt `type` und liest den Mandanten aus
`tenant_id`. Ohne beides lehnt jeder Python-Dienst den Token ab — beim Mandanten
sogar lautlos, indem ein Firmen-Akteur zur Privatperson würde.

**Was daraus wird:** beide Ansprüche fallen weg. Übrig bleibt Girders `tenant`.

**Woran man merkt, dass es Zeit ist:** kein Python-Dienst prüft mehr Token —
also erst, wenn der letzte migriert ist, nicht schon beim identity-service.

**Kosten des Vergessens:** zwei tote Ansprüche in jedem Token, und `tenant_id`
neben `tenant` ist genau die Doppelung, bei der eines Tages eines von beiden
gepflegt wird und das andere nicht.

---

## Ü-4 · Der Python-Erneuerungstoken wandert nicht mit

**Wo:** nirgends — das ist der Punkt.

**Jetzt:** Girders Erneuerungstoken ist ein undurchsichtiger Zufallswert in
`girder_refresh_tokens`, Pythons ein JWT in `sessions`. Der .NET-Dienst kann
einen Python-Erneuerungstoken nicht einlösen und antwortet `401` samt Löschen
des toten Cookies.

**Warum:** andere Form, anderer Speicher. Beides umzurechnen hieße, Girders
Sitzungsverwaltung nachzubauen, um sie einmal zu benutzen.

**Was daraus wird:** nichts. Beim Umstieg meldet sich jeder einmal neu an.

**Woran man merkt, dass es Zeit ist:** entfällt — das hier ist keine Schuld,
sondern eine einmalige Folge, die notiert ist, damit sie beim Umstieg niemanden
überrascht.

---

## Ü-5 · Der Mandant überlebt eine Erneuerung nicht von selbst

**Wo:** `session_capacities`, Migration `HandlungsformDerSitzung`, dazu
`ISessionCapacity` (Domäne) und `EfSessionCapacity` (Infrastruktur).

**Jetzt:** Pythons `sessions` trägt eine `tenant_id`, und `handle_refresh`
stellt die Firma nach erneuter Mitgliedsprüfung wieder her.
`GirderRefreshToken` hat keine solche Spalte.

**Was daraus wird:** eine eigene kleine Tabelle `SessionId → TenantId`. Weil
`SessionId` über die ganze Rotationskette stabil bleibt, ist das eine Zeile je
Anmeldung, nicht je Erneuerung. Eine fehlende Zeile heißt „handelt für sich
selbst" — die Anmeldung einer Privatperson schreibt hier gar nichts.

**Woran man merkt, dass es Zeit ist:** wenn `GirderRefreshToken` eine
Mandantenspalte bekommt. Solange nicht, bleibt die Tabelle — sie ist dann keine
Übergangsschuld mehr, sondern der Ort, an dem diese Frage beantwortet wird.

**Kosten des Vergessens:** wer für eine Firma handelt, ist eine Viertelstunde
später wieder Privatperson — lautlos, mitten in der Arbeit. Genau die
Herabstufung, vor der `docs/MIGRATION-PROMPT.md` warnt, nur durch eine andere
Tür.

**Nicht wegzukürzen:** die Mitgliedschaft wird bei **jeder** Erneuerung neu
geprüft und nie aus der Zeile geglaubt. Einmal geprüft ist der Nachweis genau
einmal gut; wer hineingelassen wurde, bliebe drin, solange er weiter erneuert —
lange nachdem das Unternehmen ihn entfernt hat.

---

## Ü-6 · bcrypt bleibt das schreibende Verfahren

**Wo:** Composition Root von `WorkerTransfer.Identity.Api`

**Jetzt:** `AddBCryptPasswords()` — bcrypt liest **und** schreibt, nicht nur
`AddBCryptPasswordReader()`.

**Warum:** Girders Kette schreibt einen Eintrag beim nächsten Anmelden neu,
sobald sie `SuccessRehashNeeded` meldet. Schriebe der .NET-Dienst dabei auf
PBKDF2 oder Argon2id um, könnte `worker_auth.BcryptPasswordHasher` diesen
Eintrag nicht mehr lesen — und die Person käme in den Python-Dienst nicht mehr
hinein. Das trifft genau den Fall, in dem man ihn braucht: einen Rückweg nach
dem Umstieg.

**Was daraus wird:** eine freie Entscheidung. Argon2id ist OWASPs erste Wahl,
und `AddArgon2Passwords()` plus `AddBCryptPasswordReader()` holt jede Person bei
ihrer nächsten Anmeldung herüber, ohne dass jemand etwas zurücksetzen muss.

**Woran man merkt, dass es Zeit ist:** der Rückweg auf den Python-identity-
service ist aufgegeben — also wenn `apps/identity-service` gelöscht wird.

**Kosten des Vergessens:** keine akuten. bcrypt mit 12 Runden ist in Ordnung;
es ist nur nicht mehr die beste verfügbare Wahl.

---

## Ü-7 · Der `CREATE TYPE`-Guard in den EF-Migrationen

**Wo:** `HandlungsformDerSitzung` (`audit_action`) und
`KontostandAlsAufzaehlung` (`account_status`) in
`dotnet/src/identity-service/…/Persistence/Migrations/`. Wortgleich in jedem
weiteren Dienst, dessen Alembic-Schema einen Postgres-Enum besitzt.

**Gemessen, damit niemand raten muss:** es sind **drei Typen in zwei Diensten**,
nicht vier. `identity-service` hat `account_status` und `audit_action`,
`consent-service` hat `consent_audit_action`. Die übrigen acht Dienste haben
keinen; was dort nach einem Enum aussieht, sind Unique-Constraints und ein
Check-Constraint (`ck_consent_events_action`).

**Jetzt:** EF braucht den Enum im Modell, sonst schickt es die Spalte als
`integer` und Postgres weist sie ab. Steht er im Modell, will die erzeugte
Migration ihn bedingungslos anlegen — auf einer Datenbank, die Alembic schon
angelegt hat. Also steht statt `AlterDatabase()` ein Guard im `Up`:

```sql
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'audit_action') THEN
        CREATE TYPE audit_action AS ENUM (…);
    END IF;
END $$;
```

**Warum ein Guard und nicht „nach Schritt 12 einmal anlegen":** der Guard ist in
beiden Welten richtig und braucht kein Gedächtnis. Eine Migration, die erst
später etwas tun soll, verlangt von jemandem, sich zum richtigen Zeitpunkt daran
zu erinnern — und genau das passiert nicht.

**Was daraus wird:** ein schlichtes `CREATE TYPE`. Nach Schritt 12 gibt es kein
Alembic mehr, das ihn vorher angelegt haben könnte, und dann ist die Bedingung
eine Bedingung über einen Fall, den es nicht mehr gibt.

**Woran man merkt, dass es Zeit ist:** `apps/identity-service` und
`apps/consent-service` sind gelöscht.

**Nachgetragen:** `account_status` brauchte denselben Guard, sobald die
Registrierung die Spalte *schreibt* statt sie nur zu lesen. Lesen ging als
Text, Schreiben nicht — Postgres nimmt in einer Enum-Spalte keinen Text an.
Damit ist `AccountStatusNames` weg: die Übersetzung macht Npgsql, und der
Test hält sie fest.

**Die Falle, und deshalb der Test:** `IF NOT EXISTS` fragt nach dem **Namen**,
nicht nach den **Werten**. Eine Datenbank, deren Etiketten von dem abweichen,
was der Dienst schreibt, läuft durch den Guard und scheitert erst bei der ersten
Einfügung — was bei `invitation_withdrawn` die erste je zurückgenommene
Einladung sein kann, Monate später. `SpaltenetikettenTests` nagelt jede Menge
deshalb dreifach fest: ausgeschrieben, gegen `pg_enum` der echten Spalte, und
gegen die SQL des Guards selbst. Gegenprobe gefahren: ein geändertes Etikett
lässt zwei der drei Tests fallen.

**Kosten des Vergessens:** ein `DO $$`-Block, der eine Bedingung prüft, die
niemand mehr verstehen muss — und, ohne den Test, ein Guard, der grün durchläuft
und den Fehler auf die erste frische Datenbank verschiebt.
