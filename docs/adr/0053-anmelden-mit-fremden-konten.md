# ADR-0053 — Anmelden mit Google, Microsoft und Apple: identity-service selbst, und nie über die E-Mail verknüpft

**Datum:** 18.10.2026 · **Status:** angenommen
**Berührt:** ADR-0006 (Passwörter), ADR-0017/0019 (Person und Firma),
ADR-0027 (Löschung), `VISION-FRAGEN-2.md` N3

## Zusammenhang

Runde 2 will die Anmeldung mit Google, Microsoft und Apple im MVP (N3). Noelia
bringt dafür nichts mit (gesucht nach Zweck am 18.10.2026: kein OpenID-Connect,
kein externer Login). Keycloak ist vor dem Start für die Control Plane
vorgesehen, nicht für die Menschen auf der Plattform.

**Gemessen am 18.10.2026**, die drei öffentlichen Discovery-Dokumente:

| Anbieter | Aussteller | `email_verified` | Name | Besonderheit |
|---|---|---|---|---|
| Google | `https://accounts.google.com` | ja | `name`, `given_name`, `family_name` | PKCE S256 angekündigt |
| Microsoft | `https://login.microsoftonline.com/{tenantid}/v2.0` — je Mandant | **nicht vorhanden** | `name` | `email` ist eine Angabe des Mandanten, nicht bestätigt |
| Apple | `https://appleid.apple.com` | ja | **nur beim ersten Mal**, nicht im Token | `is_private_email`; die Antwort kommt als POST (`form_post`) |

## Entscheidung

**identity-service ist selbst der Relying Party**, mit Authorization Code,
PKCE und Nonce — wie github-service sein OAuth selbst führt. Ein Broker
(Keycloak) wäre ein zweiter Kontospeicher und ein weiteres System im Betrieb,
für drei Knöpfe.

**Der Rückweg läuft über identity-service, nicht über die Oberfläche.** Apple
schickt die Antwort als POST an die Rückadresse; eine Seite im Browser kann
keinen POST empfangen. Für alle drei gilt deshalb dieselbe Rückadresse am
Dienst (über das Gateway): er tauscht den Code, setzt die Sitzung wie die
Passwort-Anmeldung und leitet in die Oberfläche weiter. `state`, Code-Verifier
und Nonce liegen in einem kurzlebigen, nur für den Server lesbaren Keks — so
gehört der Rückweg zu dem Browser, der ihn begonnen hat (sonst meldet ein
Angreifer sein Opfer in sein eigenes Konto an).

**Verknüpft wird über die Kennung des Anbieters, nie über die E-Mail.** Eine
Tabelle `external_logins` hält Anbieter, dessen `sub` und das Konto — keine
Tokens: sie werden für einen Austausch gebraucht und dann vergessen.

- Eine bekannte Kennung meldet an.
- Eine unbekannte Kennung, deren **bestätigte** Adresse kein Konto hat, legt
  ein **Personenkonto** an, sofort aktiv: der Anbieter hat die Adresse
  bestätigt. Das gilt für Google und Apple, **nicht für Microsoft** — dort
  fehlt `email_verified`, und ein Mandant kann jede Adresse eintragen. Ein
  Microsoft-Konto bekommt deshalb unsere eigene Bestätigungsmail, wie bei der
  Registrierung.
- Eine unbekannte Kennung, deren Adresse **schon ein Konto hat**, wird **nicht**
  verknüpft. Sonst übernimmt, wer beim Anbieter eine fremde Adresse
  eintragen kann, das Konto dahinter. Die Antwort ist dieselbe wie bei der
  Registrierung — sie verrät nicht, ob es das Konto gibt. Verbinden kann man
  einen Anbieter nur angemeldet, in den Einstellungen.

**Eine Firma entsteht so nicht.** Firmen entstehen bei der Registrierung aus
einer bestätigten Firmenadresse (ADR-0019); wer sich mit Google anmeldet, ist
eine Person. Zu einer Firma kommt man wie bisher über eine Einladung.

**Nur das, wofür es angebunden ist.** Angefragt werden `openid email` und —
für die Anrede — `profile` bzw. Apples `name`; nichts darüber hinaus, kein
Zugriff auf Kalender, Dateien, Kontakte. Der Name wird nur beim Anlegen
übernommen, und die Person kann ihn ändern.

**Leer heißt aus.** Ohne `ExternalLogin__<Anbieter>__ClientId` erscheint der
Knopf nicht, wie bei GitHub. Die Ziel-Hosts stehen in der Konfiguration und
damit in der Egress-Grenze.

**Löschung.** `external_logins` gehört zum Konto und fällt mit ihm; identity
ist der letzte Empfänger der Kaskade (ADR-0027).

## Was vor dem Bau gebraucht wird

- Je Anbieter eine App-Registrierung auf den Betreiber: Google Cloud Console,
  Microsoft Entra, Apple Developer Program (kostenpflichtig; Apples
  Client-Secret ist ein selbst signiertes JWT mit höchstens sechs Monaten
  Laufzeit). Bis dahin wird gegen einen nachgebauten Anbieter in den Tests
  gemessen, und der Knopf bleibt aus.

## Was es kostet

- identity-service spricht drei fremde Hosts; eine Tabelle mehr.
- Ein Konto kann mehrere Anmeldewege haben; eine Seite „Anmeldewege“ in den
  Einstellungen zeigt und trennt sie.
- Die Nutzungsbedingungen der drei Anbieter prüft der Jurist.
