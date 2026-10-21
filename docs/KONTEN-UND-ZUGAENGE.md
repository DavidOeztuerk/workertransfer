# Konten und Zugänge — was wo eingerichtet ist

Stand **20.10.2026**. Diese Datei nennt Orte, Konten und Schlüssel**namen** —
nie einen Wert und nie eine private Adresse. Die Werte stehen in der
git-ignorierten `.env`, geschrieben über `scripts/env-secret.sh`.

Alles unten läuft heute auf **privaten Konten des Betreibers**. Wie das später
auf die Firma umzieht, steht im zweiten Teil.

---

## 1. Was eingerichtet ist

### Google Cloud — Projekt `workertransfer`

| Was | Wo |
|---|---|
| Projekt-Übersicht | <https://console.cloud.google.com/home/dashboard?project=workertransfer> |
| OAuth-Clients („Anmelden", „Dateiauswahl") | <https://console.cloud.google.com/auth/clients?project=workertransfer> |
| Zustimmungsbildschirm (Name, Logo, Links) | <https://console.cloud.google.com/auth/branding?project=workertransfer> |
| Zielgruppe, Testnutzer, Veröffentlichen | <https://console.cloud.google.com/auth/audience?project=workertransfer> |
| API-Schlüssel des Pickers | <https://console.cloud.google.com/apis/credentials?project=workertransfer> |
| Aktivierte APIs (Drive, Picker) | <https://console.cloud.google.com/apis/dashboard?project=workertransfer> |

- **Modus: Test.** Anmelden können nur eingetragene Testnutzer (höchstens 100).
- **`.env`:** `WORKERTRANSFER_GOOGLE_CLIENT_ID`, `…_CLIENT_SECRET`,
  `WORKERTRANSFER_GOOGLE_PICKER_CLIENT_ID`, `…_PICKER_API_KEY`, `…_PICKER_APP_ID`.
- **Kosten:** 0 €. Anmeldung, Drive API und Picker API sind kostenlos; es
  hängt kein Abrechnungskonto am Projekt.

### Microsoft Entra — App „WorkerTransfer – Anmelden"

| Was | Wo |
|---|---|
| App-Registrierungen | <https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade> |
| Geheimnis erneuern | in der App → „Zertifikate & Geheimnisse" |

- Liegt im **Standardverzeichnis des privaten Microsoft-Kontos**.
- Kontotypen: alle Entra-Mandanten und persönliche Microsoft-Konten.
- **`.env`:** `WORKERTRANSFER_MICROSOFT_CLIENT_ID`, `…_CLIENT_SECRET`.
- **Das Geheimnis läuft am 18.04.2027 ab.** Danach scheitert die Anmeldung
  mit Microsoft still; ein neues anlegen und über
  `scripts/env-secret.sh --clipboard WORKERTRANSFER_MICROSOFT_CLIENT_SECRET`
  eintragen.
- **Kosten:** 0 € (Entra ID Free).

### Dropbox — App „WorkerTransfer"

| Was | Wo |
|---|---|
| App Console | <https://www.dropbox.com/developers/apps> |

- Zugriff: nur App-Ordner. Der Chooser braucht nur den App-Key.
- Chooser-Domain: `localhost`.
- **`.env`:** `WORKERTRANSFER_DROPBOX_APP_KEY`.
- **Kosten:** 0 €.

### XING — Datenauskunft

| Was | Wo |
|---|---|
| Datenarchiv | <https://www.xing.com/settings/privacy/data/disclosure> |

Angefordert am 20.10.2026; XING meldet sich per E-Mail oder SMS. Danach wird
das Format gemessen und der Import gebaut (DRITTANBIETER.md).

### Apple — aufgeschoben

Kostet 99 € im Jahr und braucht eine echte Domain mit https. Der Code ist
fertig und aus, bis `WORKERTRANSFER_APPLE_*` gesetzt ist.

### Lokal

| Was | Adresse |
|---|---|
| Oberfläche | <http://localhost:5173> |
| Gateway (API) | <http://localhost:8090> |
| Mailpit (alle Mails des Stapels) | <http://localhost:8025> |
| Control Plane | <http://localhost:8091> (`make control-plane-oidc`) |
| Keycloak-Verwaltung | <https://localhost:8443/admin> (`make keycloak`) |

Keycloaks Geheimnisse (Admin, Client, Bediener `operator`) stehen in
`.control-plane/keycloak.env`.

---

## 2. Wenn die Domain da ist

Angenommen, die Oberfläche läuft unter `https://<domain>` und die API unter
`https://api.<domain>`. Dann ändert sich:

| Wo | Was |
|---|---|
| `.env` / Chart | `WORKERTRANSFER_PUBLIC_URL`, `WORKERTRANSFER_WEB_URL`, `CORS_ORIGINS`, `WT_API_ORIGIN` (CSP) |
| Google, Client „Anmelden" | Rückweg `https://api.<domain>/auth/external/google/callback` dazu |
| Google, Client „Dateiauswahl" | Herkunft `https://<domain>` dazu |
| Google, API-Schlüssel | Website `https://<domain>/*` dazu |
| Google, Branding | Startseite, Datenschutzerklärung, Nutzungsbedingungen, autorisierte Domain — dann **veröffentlichen** |
| Microsoft | Rückweg `https://api.<domain>/auth/external/microsoft/callback` dazu; Herausgeber verifizieren (unten) |
| Dropbox | Chooser-Domain `<domain>` dazu |
| Apple | jetzt erst: Developer Program, Services ID, Rückweg über https |
| Keycloak / Control Plane | echte Authority mit Zertifikat; Lizenz an diese Authority binden |

`localhost` darf überall stehen bleiben — dann funktioniert die Entwicklung
weiter mit denselben Clients.

---

## 3. Vom privaten Konto zur Firma

**Der Grundsatz:** Die Firma besitzt, Menschen verwalten. Kein Zugang hängt
an einer einzelnen privaten Adresse. Mindestens **zwei** Administratoren,
überall ein zweiter Faktor. Kontaktadressen sind Rollen (`technik@`,
`datenschutz@`), keine Personen — wer geht, nimmt sonst die Zustellung mit.

**Eine Firmenadresse genügt für alles.** Ein Google-Konto braucht keine
Gmail-Adresse, ein Microsoft-Konto keine Outlook-Adresse, Dropbox und Apple
nehmen jede Adresse. Bei der Registrierung jeweils „eigene/bestehende
E-Mail-Adresse verwenden" wählen.

| Anbieter | Profi-Weg | Muss neu angelegt werden? |
|---|---|---|
| **Google** | **Cloud Identity Free** (0 €, bis 50 Nutzer) mit der Firmendomain → eine Google-Cloud-**Organisation**. Firmenkonten darin anlegen, das Projekt in die Organisation verschieben, das private Konto entfernen. | **Nein** — Clients, Schlüssel und Werte bleiben. Domain-Nachweis per DNS-TXT. |
| **Microsoft** | Ein **eigener Entra-Mandant der Firma** (Entra ID Free, 0 €) mit verifizierter Firmendomain. Für die Herausgeber-Verifizierung eine **Partner-One-ID** (früher MPN-ID) aus dem Microsoft AI Cloud Partner Program; deren Domain muss zum Mandanten passen. | **Ja** — eine App-Registrierung zieht nicht zwischen Mandanten um. Neu registrieren dauert fünf Minuten (`/einrichten microsoft`), danach neue Werte in die `.env`. |
| **Dropbox** | Firmenkonto. | Besitzwechsel nur per Support-Ticket; **neu anlegen ist schneller** (zwei Minuten, neuer App-Key). |
| **Apple** | Als **Organisation** einschreiben: braucht eine juristische Person, eine D-U-N-S-Nummer (kostenlos) und eine Website. Einzelunternehmer schreiben sich als Person ein. | Noch nichts angelegt. |

**Reihenfolge:** Domain → Firmenpostfach → Google-Organisation (Projekt
umziehen) → Microsoft-Mandant (App neu) → Dropbox (App neu) → Apple. Und
**Q2 (Betreiber) vor Apple und vor der Partner-One-ID**: beide fragen nach der
Rechtsform (`ENTSCHEIDUNG-VOR-DEM-START.md`).

---

## 4. Das Firmenpostfach

Für die Registrierungen muss die Adresse nur **empfangen** können. Für Mails
an Kundinnen, Bewerbende und Behörden muss sie auch **senden** — spätestens
mit dem Start.

| Weg | Kosten (Listenpreise Okt. 2026, ungeprüft) | Passt, wenn … |
|---|---|---|
| Postfach beim Domain-Anbieter (z. B. IONOS Mail Basic) | ab ≈ 1–1,50 €/Monat, Domain extra | Domain und Mail aus einer Hand, deutscher Anbieter, AV-Vertrag |
| mailbox.org | ab ≈ 1–3 €/Monat, eigene Domain je nach Tarif | Datenschutz als Kern, deutsches Rechenzentrum |
| Zoho Mail Free | 0 €, 5 Nutzer, 5 GB, nur Weboberfläche | es wirklich nichts kosten darf |
| nur Weiterleitung (beim Registrar oder Cloudflare Email Routing) | 0 € | für den Übergang: empfängt, sendet aber nicht unter der Firmenadresse |
| Google Workspace / Microsoft 365 | pro Nutzer und Monat, deutlich mehr | später, wenn ein Team zusammenarbeitet |

**Vorschlag:** Domain und Postfach bei einem deutschen Anbieter mit
Auftragsverarbeitungsvertrag. Bei einer Plattform, die Datenschutz verspricht,
landen an `datenschutz@` die Auskunfts- und Löschbitten der Nutzer — ein
kostenloser Dienst eines Drittlands ist dafür der falsche Ort, auch wenn er
technisch reicht.

Quellen: [Cloud Identity Free (costbench)](https://costbench.com/software/identity-access-management/google-cloud-identity/free-plan/),
[Eigenständige Organisation (Google)](https://docs.cloud.google.com/resource-manager/docs/standalone-organization-overview?hl=de),
[Dropbox-App übertragen (Forum)](https://www.dropboxforum.com/discussions/101000042/moving-a-dropbox-app-from-one-account-to-another/764526),
[Herausgeber-Verifizierung (Microsoft Learn)](https://learn.microsoft.com/en-ca/answers/questions/1805115/unable-to-find-add-partner-one-id-to-verify-publis),
[Partner One ID (entradiff)](https://entradiff.merill.net/changes/2026-09-03/publish-app-gallery-08/),
[Postfach-Vergleich (mailvergleich.de)](https://mailvergleich.de/email-domain-kaufen/),
[Kostenlose E-Mail-Domain (mailvergleich.de)](https://mailvergleich.de/ratgeber/kostenlose-email-domain/).
