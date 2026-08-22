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
