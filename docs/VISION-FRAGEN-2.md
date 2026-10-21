# Vision-Fragen, Runde 2

Stand 16.10.2026, **beantwortet am 17.10.2026** — zusammen im Gespräch. Runde 1 (`VISION-FRAGEN.md`) ist beantwortet und gebaut,
bis auf die Vertragsvorlagen (blockiert durch den Juristen, J7). Diese Runde
holt nach, was aus dem ursprünglichen Auftrag (`docs/vision/kon.txt`) weder
gebaut noch geklärt ist, und die Punkte, die wir auf „vor dem Start“
verschoben haben.

## So beantwortest du ihn

- Wie in Runde 1: Antwort direkt unter **Antwort:**. Ein Buchstabe genügt.
- **„Weiß nicht“ ist eine gute Antwort.** Dann recherchiere ich weiter.
- Neu: Unter vielen Fragen steht **Vorschlag** — was ich bauen würde und
  warum. Wenn du zustimmst, reicht „Vorschlag“.
- Die Buchstaben gehen bei **L** weiter, damit sich nichts mit Runde 1
  überschneidet (dort A–K).
- **Recht** ist eine Einordnung, keine Rechtsberatung. Neue Juristenfragen
  stehen am Ende (J14–J17).

---

## L. Karriereseiten-Anbindung — Inserate von der Firmenwebsite holen

**Heute:** Inserate entstehen nur auf der Plattform, von Hand oder mit der
KI-Hilfe für den Text. Nichts wird von außen geholt (ADR-0004: kein Scraping).
**Grundsatz aus dem Auftrag:** nur über offizielle Schnittstellen, keine
Nutzungsrechte verletzen.

### L1. Welche Quellen sind erlaubt?

**Was es gibt (nach heutigem Stand, vor dem Bau zu messen):**
- **Bewerbermanagement-Systeme (ATS) mit dokumentiertem, öffentlichem
  Stellenabruf:** Greenhouse (Job-Board-API), Lever (Postings-API), Personio
  (XML-Feed, den die Firma in ihren Personio-Einstellungen selbst
  einschaltet), Recruitee, SmartRecruiters, softgarden u. a.
- **Workday, SAP SuccessFactors, iCIMS:** kein allgemein offener Abruf, meist
  nur über Partnerverträge.
- **Strukturierte Stellendaten auf der eigenen Website** der Firma
  (schema.org `JobPosting` — dasselbe Format, das Google for Jobs liest).
- **Jobbörse der Arbeitsagentur: fällt raus.** Laut dem Projekt bundesAPI
  bietet die Bundesagentur keine offizielle Schnittstelle an; der
  verbreitete Zugang ist nachgebaut.

**Optionen:**
- A) Nur ATS-Schnittstellen, die der Anbieter dokumentiert.
- B) A + strukturierte Stellendaten von der eigenen, nachgewiesenen Domain
  der Firma.
- C) A + eine Feed-Adresse (XML/JSON), die die Firma selbst einträgt.
- D) Gar nicht — Inserate nur auf der Plattform.

**Recht:** Ein Abruf **im Auftrag der Firma** von **ihren eigenen** Inseraten
ist kein Scraping fremder Inhalte. Trotzdem gelten die Bedingungen des
ATS-Anbieters (J14).

**Vorschlag:** A + C zuerst, B später.

**Antwort:** **A + C, 17.10.2026 — MVP.** Offizielle Stellenabrufe der ATS-Anbieter und eine Feed-Adresse, die die Firma selbst einträgt. Stellendaten der Website (B) später.

### L2. Wie wird bewiesen, dass die Quelle der Firma gehört?

**Problem:** ATS-Seiten liegen auf fremden Domains (`firma.jobs.personio.de`).
Ohne Nachweis könnte jede Firma die Inserate einer anderen unter eigenem Namen
einspielen.

**Optionen:**
- A) Ein **DNS-Eintrag** (TXT) auf der Domain der Firma, einmal eingerichtet
  — derselbe Nachweis trägt auch die Subdomain aus M3.
- B) Die Karriereseite auf der eigenen Domain verlinkt die Quelle, die
  Plattform prüft den Link.
- C) Kein Nachweis, ein Admin trägt ein.

**Vorschlag:** A.

**Antwort:** ~~A, DNS-TXT~~ — **korrigiert am 17.10.2026: B, MVP.** Ein
DNS-Eintrag beweist nur die Domain (die ist per E-Mail schon bewiesen), nicht
dass ein Board der Firma gehört: eine fremde Firma könnte „greenhouse:acme“
in ihr eigenes DNS schreiben. Deshalb: eine Seite auf der nachgewiesenen
Domain verlinkt das Board, geprüft beim Einrichten und vor jedem Abruf; ein
eigener Feed muss auf der eigenen Domain liegen.

**Noch einmal korrigiert am 18.10.2026: ein Code im Board** (ADR-0052). Der
Link auf der eigenen Seite hat dasselbe Loch wie der DNS-Eintrag — Firma B kann
genauso auf Acmes Board verlinken. Beweisen kann nur, was allein der Inhaber
des Boards ändern kann: die Firma schreibt einen Code der Plattform in ein
Inserat in ihrem ATS. Der eigene Feed bleibt durch seinen Ort bewiesen. Ein
echter Login beim ATS kommt nach dem MVP (`DRITTANBIETER.md`).

### L3. Wie kommen geholte Inserate auf die Plattform?

**Optionen:**
- A) Als **Entwurf**; ein Admin veröffentlicht jedes einzeln.
- B) Direkt veröffentlicht.
- C) Die Firma wählt pro Quelle: Entwurf oder direkt.

Dazu: Verschwindet ein Inserat an der Quelle, wird es hier **geschlossen**
(nicht gelöscht — Bewerbungen darauf bleiben).

**Vorschlag:** C, Vorgabe „Entwurf“.

**Antwort:** **C, 17.10.2026 — MVP.** Die Firma wählt pro Quelle; Vorgabe ist „Entwurf“. Verschwindet ein Inserat an der Quelle, wird es hier geschlossen.

### L4. Wie oft wird abgeholt?

**Optionen:**
- A) Nur auf Klick.
- B) Einmal täglich, dazu auf Klick.
- C) Stündlich.

**Vorschlag:** B.

**Antwort:** **B, 17.10.2026 — MVP.** Einmal täglich, dazu „jetzt abholen“ auf Klick.

### L5. Wohin geht eine Bewerbung auf ein geholtes Inserat?

**Optionen:**
- A) Auf die Plattform, wie jede Bewerbung — die Firma liest sie hier.
- B) Weiterleitung zum Bewerbungslink im ATS — die Person bewirbt sich dort,
  die Plattform erfährt nichts davon.
- C) Die Firma wählt pro Quelle.

**Was B kostet:** Mappe, Anschreiben-Agent, Einwilligungsregister und
Löschversprechen greifen dort nicht — die Person verlässt die Plattform.

**Vorschlag:** A, C als spätere Option.

**Antwort:** **A, 17.10.2026 — MVP.** Die Bewerbung kommt auf die Plattform, wie jede andere. Wahl pro Quelle (C) später.

---

## M. Persönliche Seite für eine Person unter der Firmen-Subdomain

**Aus dem Auftrag:** Die Firma verknüpft eine Subdomain per DNS
(`karriere.firma.de/…`), und jede Person bekommt dort eine eigene Seite, damit
sie „ein besseres Gefühl“ für die Firma bekommt.

### M1. Für wen gibt es eine solche Seite?

**Optionen:**
- A) Für jede Person, die sich beworben hat.
- B) Für eine Person, die die Firma angesprochen hat (Ansprache, ADR-0048).
- C) Beides.
- D) Nicht bauen.

**Antwort:** **Nach dem MVP, 17.10.2026.** Die persönliche Seite kommt in der nächsten Version; M3 und M4 stehen als Richtung fest, M2 und M5 werden dann entschieden.

### M2. Was steht darauf? (mehrere möglich)

- [ ] Begrüßung mit dem Namen der Person
- [ ] Ansprechpartner (Name, Foto, Rolle)
- [ ] Die nächsten Schritte im Ablauf
- [ ] Team oder Abteilung
- [ ] Video der Firma
- [ ] Benefits
- [ ] Stand der Bewerbung
- [ ] Direkt bewerben / antworten

**Antwort:** Mit M1 nach dem MVP — dann entscheiden.

### M3. Unter welcher Adresse?

**Optionen:**
- A) Nur unter der Subdomain der Firma (per DNS auf WorkerTransfer,
  Zertifikat automatisch).
- B) Nur unter WorkerTransfer (`workertransfer.de/firma/…`) — ohne DNS.
- C) B sofort für alle, A für Firmen, die ihre Subdomain einrichten.

**Was A kostet:** ein Zertifikat pro Firmendomain (automatisch über Let's
Encrypt) und den Domain-Nachweis aus L2 — sonst kann jemand eine fremde Domain
auf uns zeigen lassen.

**Vorschlag:** C.

**Antwort:** **C, 17.10.2026 — Richtung für nach dem MVP.** Sofort unter WorkerTransfer, Subdomain für Firmen, die sie einrichten.

### M4. Wer kommt auf die Seite, und was verrät die Adresse?

**Risiko:** Der Name der Person in der Adresse (`karriere.firma.de/anna-schmidt`)
landet in Suchmaschinen, im Browserverlauf, auf einem geteilten Bildschirm beim
jetzigen Arbeitgeber.

**Optionen:**
- A) Nur nach Anmeldung, nur die Person.
- B) Ein geheimer, nicht erratbarer Link ohne Namen, der abläuft; nie in
  Suchmaschinen.
- C) B für den allgemeinen Teil, A für alles mit ihrem Namen.

**Vorschlag:** C.

**Antwort:** **C, 17.10.2026 — Richtung für nach dem MVP.** Allgemeiner Teil über einen geheimen, ablaufenden Link ohne Namen, alles mit Namen nach Anmeldung.

### M5. Erfährt die Firma, ob die Person die Seite angesehen hat?

**Optionen:**
- A) Nein — wie es auch keinen Gelesen-Status bei Nachrichten gibt (ADR-0047).
- B) Nur ob überhaupt.
- C) Ja, mit Zeit und Dauer.

**Recht:** Messen, wer was ansieht, braucht eine Einwilligung (§ 25 TDDDG) —
und es ist Druck auf die Person.

**Vorschlag:** A.

**Antwort:** Mit M1 nach dem MVP — dann entscheiden. Vorgabe bis dahin: kein Gelesen-Status (ADR-0047).

---

## N. LinkedIn, XING und weitere Verbindungen

### N1. LinkedIn

**Fakten:** „Sign in with LinkedIn“ (OpenID Connect) liefert nur Name, Foto und
E-Mail. Werdegang und Fähigkeiten gibt LinkedIn über die offene Schnittstelle
nicht heraus, nur an Partner. **Was jede Person selbst kann:** bei LinkedIn
ihren Datenexport anfordern (eine ZIP-Datei mit Positionen, Ausbildung,
Fähigkeiten) und hier hochladen.

**Optionen (mehrere möglich):**
- A) Anmelden mit LinkedIn.
- B) Import aus dem **eigenen** LinkedIn-Export — als **Vorschläge** für
  Lebenslauf und Fähigkeiten, wie die Vorschläge aus Dokumenten (ADR-0043):
  ein Klick füllt das Feld, erst Speichern macht es zur Angabe. Die Datei wird
  nach dem Lesen nicht aufbewahrt.
- C) Nur ein Link auf das eigene LinkedIn-Profil.
- D) Nichts.

**Vorschlag:** B + C. A erst mit dem Identitätsanbieter aus I2.

**Antwort:** **A + B + C, 17.10.2026 — nach dem MVP.** Import aus dem eigenen Export, Link aufs Profil und Anmelden mit LinkedIn, alles in der nächsten Version.

### N2. XING

**Fakten:** Eine offene Profil-Schnittstelle ist mir nicht bekannt; der Stand
ist vor dem Bau zu messen. Einen Datenexport muss jedes Netzwerk herausgeben
(Art. 20 DSGVO).

**Optionen:**
- A) Nur ein Link auf das eigene Profil.
- B) Import aus dem eigenen Export, wie bei LinkedIn (wenn das Format
  brauchbar ist).
- C) Nichts.

**Vorschlag:** A, B nach Messung.

**Antwort:** **A, B nach Messung, 17.10.2026 — MVP.** Ein Link aufs eigene XING-Profil; ein Import aus dem eigenen Export, wenn das Format brauchbar ist.

**Gebaut am 18.10.2026 (der Link).** Nur eine XING-Profiladresse, gespeichert als `https://www.xing.com/profile/<name>`. **Der Link nennt die Person** — ihren Namen gibt die Plattform sonst zuletzt frei (ADR-0037 Stufe 3). Deshalb sagt das Formular vor dem Speichern, dass jeder, der das Profil sehen darf, damit weiß, wer sie ist; der Link reist nur mit dem vollen Profil, nie in den Treffern des Scouts. Der Import wartet auf einen echten XING-Export zum Messen.

### N3. Weitere Verbindungen (mehrere möglich, jeweils ja / nein / später)

| Verbindung | Wofür | Antwort |
|---|---|---|
| Anmelden mit Google / Microsoft / Apple | weniger Passwörter | |
| Dokumente aus Google Drive / OneDrive / Dropbox | Zeugnisse holen, nur die gewählte Datei, kein Ordnerzugriff | |
| Open Badges / Credly | Zertifikate mit prüfbarem Nachweis (z. B. Schweißerprüfung, Kurse) | |
| GitLab | wie GitHub heute | |
| Videotermin-Link (Teams / Zoom / Meet) | Link im Gespräch, kein Konto-Zugriff | |
| Rückweg ins ATS der Firma | eine Bewerbung landet auch in Personio & Co. | |

Schon entschieden und nicht neu gefragt: **Unterschrift** außerhalb (F3),
**Termine** als eigene kleine `.ics`-Lösung später (E1).

**Antwort:** **17.10.2026.** **MVP:** Anmelden mit Google / Microsoft / Apple; Dokumente aus Google Drive / OneDrive / Dropbox (nur die gewählte Datei). **Nach dem MVP:** Open Badges / Credly, GitLab, Videotermin-Link, Rückweg ins ATS.

**Anmelden gebaut am 18.10.2026** (ADR-0053). Die Knöpfe erscheinen, sobald der Betreiber die drei Apps registriert hat. Eine Adresse, zu der es schon ein Konto gibt, wird nie automatisch verknüpft — verbinden geht angemeldet in den Einstellungen; Microsoft-Konten bekommen unsere Bestätigungsmail, weil Microsoft keine Adresse bestätigt.

**Dateien gebaut am 18.10.2026** (ADR-0054): Google Drive und Dropbox, nur die gewählte Datei. **OneDrive nicht** — gemessen: sein Picker verlangt Lesezugriff auf alle Dateien, und die Antwort hier sagt „nur die gewählte Datei“.

### N4. MCP — Anbindung an KI-Assistenten

**Optionen:**
- A) WorkerTransfer bietet einen **MCP-Server für die Person**: Sie verbindet
  ihren eigenen KI-Assistenten (Claude, ChatGPT …) mit **ihren eigenen** Daten,
  mit eigenem Schlüssel, jederzeit widerrufbar.
- B) Für Firmen nur mit **Firmendaten** (Inserate, Stand der Vorgänge) —
  **nie** Personendaten, die die Firma über die Plattform sieht.
- C) Die Agenten der Plattform nutzen fremde MCP-Server (Kalender, Mail).
- D) Später.

**Recht:** Bei A gibt die Person ihre Daten an einen Anbieter ihrer Wahl —
ihre Entscheidung, aber dort gelten dessen Regeln (J17). Bei B mit
Personendaten würde eine Firma fremde Daten an einen KI-Dienst weitergeben.

**Vorschlag:** D vor dem Start; danach A, dann B.

**Antwort:** **D, 17.10.2026 — nach dem MVP:** zuerst ein MCP-Zugang für die Person zu ihren eigenen Daten, danach für Firmen nur mit Firmendaten.

---

## O. Weitere Agenten

**Grundregeln, schon entschieden (D2, D3, ADR-0022):** Ein Agent entwirft, ein
Mensch gibt frei. Kein Agent schickt selbst, keiner macht selbst Gegenangebote,
keiner bewertet Personen für eine Firma.

### O1. Welche Agenten sollen kommen? (ja / nein / später)

**Für die Person:**

| Agent | Was er tut | Grenze | Antwort |
|---|---|---|---|
| Interview-Trainer | übt mit ihr für eine bestimmte Stelle, Fragen aus dem Inserat | Rückmeldung nur an sie, keine Note, nichts gespeichert | |
| Lebenslauf-Optimierer | schlägt bessere Formulierungen für ihren Lebenslauf vor | Vorschlag, sie übernimmt | |
| Lern-Coach | zeigt zu einer Stelle, welche genannte Fähigkeit fehlt, und wo man sie lernt | Häkchen statt Prozent | |
| Dokumentenmanager | ordnet Unterlagen, erinnert an ablaufende Nachweise (Staplerschein, Schweißerprüfung) | nur ihre eigenen Unterlagen | |
| Portfolio-Helfer | formuliert Texte zu ihren Arbeitsproben | Vorschlag | |
| Karriere-Coach | allgemeines Gespräch über die berufliche Richtung | kein Rechtsrat | |

**Für die Firma:**

| Agent | Was er tut | Grenze | Antwort |
|---|---|---|---|
| Interview-Vorbereitung | Fragen zum **Inserat** für das Gespräch | nie über die Person | |
| Angebots-Entwurf | Angebotstext entwerfen | Admin gibt frei | |
| Personalplanung | Stellen und Bedarf der eigenen Firma | nur Firmendaten | |

Schon gebaut: Anschreiben-Agent, KI-Berater, KI-Scout und Recruiter-Entwurf,
Inserat-Hilfe, Vorschläge aus Dokumenten.

**Antwort:** **Alle nach dem MVP, 17.10.2026.** Keiner der neun Agenten kommt vor dem Start; die Grenzen in den Tabellen gelten, wenn sie gebaut werden.

### O2. Diese Agenten aus dem Auftrag werden nicht gebaut — bestätigst du?

- **Candidate Ranking, Match Score, Wechselwahrscheinlichkeit** — eine Zahl
  oder Reihenfolge über Menschen (ADR-0022).
- **Gehaltsempfehlung für eine Person an die Firma** — dieselbe Zahl.
- **Interview-Agent, der Bewerber bewertet** — Hochrisiko-KI (EU AI Act,
  Anhang III Nr. 4), D3.
- **Team-Analyse über Personen** — Leistungsbewertung von Beschäftigten.

**Antwort:** **Bestätigt, 17.10.2026.** Diese vier Arten werden nie gebaut.

### O3. Agenten von Drittanbietern (Marktplatz)?

**Optionen:**
- A) Nie.
- B) Später, mit denselben Regeln und einer Prüfung jedes Agenten.
- C) Weiß nicht.

**Vorschlag:** B, nicht vor dem Start.

**Antwort:** **B, 17.10.2026 — nach dem MVP**, mit denselben Regeln und einer Prüfung jedes Agenten.

---

## P. Der JobPilot-Kern: viele Bewerbungen auf einmal

**Heute:** Mehrere Inserate auswählen → der Anschreiben-Agent entwirft → die
Person prüft und gibt frei → abschicken, mit Mappe (Lebenslauf, Zeugnisse).
Das gilt **nur für Inserate auf der Plattform**.

### P1. Bewerbungen an Firmen außerhalb der Plattform, per E-Mail?

**Optionen:**
- A) Nein — nur Inserate auf der Plattform (und geholte, L).
- B) Ja: Die Person trägt Inserat und Bewerbungsadresse ein, die Plattform
  verschickt in ihrem Namen, mit Anhängen.
- C) Ja, aber **aus ihrem eigenen Postfach** (Gmail/Outlook-Anbindung) — die
  Mail kommt wirklich von ihr.

**Was B kostet:** Die Mail kommt technisch von WorkerTransfer (Antworten gehen
an sie); Spamfilter und Zustellbarkeit werden ein eigenes Thema.
Einwilligungsregister und Löschversprechen enden an der Mail.

**Vorschlag:** A vor dem Start; C später, wenn gewünscht.

**Antwort:** **A, 17.10.2026.** Nicht im MVP; später Versand aus dem eigenen Postfach (C).

### P2. Vorlagen fürs Anschreiben?

**Heute:** Lebenslauf mit zwei Vorlagen (Schlicht, Klassisch); das Anschreiben
schreibt der Agent als Text.

**Optionen:**
- A) Reicht so.
- B) Gestaltungsvorlagen auch fürs Anschreiben, passend zum Lebenslauf.
- C) B + „Motivationsschreiben“ als eigene Art.

**Antwort:** **B, 17.10.2026 — MVP.** Gestaltungsvorlagen fürs Anschreiben, passend zu den Lebenslauf-Vorlagen.

**Gebaut am 18.10.2026.** Keine eigene Wahl fürs Anschreiben: es folgt der Vorlage, die auf der Lebenslaufseite gewählt ist — eine Mappe, deren Brief und Lebenslauf aussehen wie von zwei Leuten, liest sich schlechter als jedes für sich.

---

## Q. Vor dem Start — aus Runde 1 verschoben

**Vorbereitet am 20.10.2026:** [`ENTSCHEIDUNG-VOR-DEM-START.md`](ENTSCHEIDUNG-VOR-DEM-START.md)
legt Q1–Q3 mit Kosten, Folgen im Code und einer Messung des Stapels
nebeneinander — entschieden ist damit nichts.

### Q1. Geschäftsmodell (G1, G2)

In Runde 1 bewusst als letzter Punkt vor dem Start vorgemerkt. Du kannst die
Richtung jetzt festlegen oder weiter zurückstellen.

**Optionen:**
- A) Abo für Firmen, Personen zahlen nichts.
- B) A + Provision bei einem Wechsel mit Ablöse.
- C) Provision pro Einstellung statt Abo.
- D) Weiter zurückstellen bis kurz vor dem Start.

**Recht:** Private Arbeitsvermittlung (§§ 296 ff. SGB III); von
Arbeitsuchenden höchstens 2.000 €, von Firmen frei.

**Vorschlag:** A oder B — Personen zahlen nie, sonst verdient die Plattform an
der Person, für die der Berater arbeitet.

**Antwort:** **D, 17.10.2026.** Weiter zurückgestellt — letzter Punkt vor dem Start.
**Entschieden am 20./21.10.2026** in [`GESCHAEFTSMODELL-FRAGEN.md`](GESCHAEFTSMODELL-FRAGEN.md):
Plattform kostenlos; Firmen zahlen 500 € netto je Einstellung, fällig nach der
Probezeit; KI kostet für alle (eigener Schlüssel oder KI-Guthaben bei uns).
**Idee dazu, 17.10.2026 (zur KI, ADR-0051):** Die Plattform hat **einen**
KI-Anbieter (z. B. Anthropic), und Person wie Firma nutzen ihn per **Abo** oder
über ein **gekauftes Kontingent**. Harte Bedingung: ein Anschreiben darf nie
mitten im Schreiben am leeren Kontingent scheitern — also den Bedarf vor dem
Start reservieren, danach abrechnen. Der Preis muss die Kosten tragen, ohne
dass jemand nicht mehr alles machen kann. Bis zum Start bleibt Ollama der
Standard; wer einen eigenen Schlüssel (Anthropic, OpenAI) hinterlegt, nutzt den.

### Q2. Wer betreibt die Plattform?

Für Impressum, Datenschutzerklärung (Verantwortlicher) und Gewerbeanzeige.

**Optionen:**
- A) Du als Einzelunternehmer.
- B) Eine GmbH oder UG.
- C) Noch offen.

**Antwort:** **C, 17.10.2026.** Noch offen — vor dem Start entscheiden.

### Q3. Wo läuft sie?

**Optionen:**
- A) Bei einem Anbieter mit Sitz in Deutschland oder der EU (z. B. Hetzner,
  IONOS, STACKIT).
- B) Bei einem großen US-Anbieter in einer EU-Region.
- C) Noch offen.

**Vorschlag:** A — passt zu Mistral (EU) und zur Egress-Grenze, kein
Drittland für die Daten.

**Antwort:** **C, 17.10.2026.** Noch offen — vor dem Start entscheiden.

### Q4. Control Plane im Betrieb (I2)

Vorgemerkt: eigener Identitätsanbieter (z. B. Keycloak) und eine echte Lizenz.

**Optionen:**
- A) So, vor dem Start.
- B) Anders: …

**Antwort:** **A, 17.10.2026.** Eigener Identitätsanbieter (z. B. Keycloak) und echte Lizenz, vor dem Start.

**Lokal gebaut am 20.10.2026:** `make keycloak && make control-plane-oidc`. Offen bis zum Start: die Domain, ihr Zertifikat und die an diese Authority gebundene Lizenz.

### Q5. Ein Arbeitgeber gibt Personal aktiv ab (C1, eigener Fall)

**Vorschlag aus Runde 1:** Der Arbeitgeber **lädt** seine Leute ein, sich
„offen für Wechsel“ zu stellen, und sieht nur **wie viele**, nicht **wer**.
Wer zustimmt, ist sichtbar wie jede Person, die zuhört, optional mit dem
Vermerk „vom bisherigen Arbeitgeber unterstützt“. Nie ohne Zustimmung, nie das
Wort „Insolvenz“.

**Optionen:**
- A) So bauen.
- B) Nicht bauen.
- C) Später.

**Antwort:** **C, 17.10.2026 — so bauen, nach dem MVP.**

### Q6. Person ↔ alter Arbeitgeber auf der Plattform (D1, dritter Weg)

**Heute:** Der Arbeitgeber kommt nur über die Ablöse-Verhandlung mit der neuen
Firma vor (ADR-0050). Aufhebung und Zeugnis klärt die Person selbst
(Checklisten, Musterschreiben).

**Optionen:**
- A) Ein eigenes Gespräch Person ↔ Arbeitgeber über den geöffneten Weg; der
  Berater entwirft, sie gibt frei.
- B) Nicht auf der Plattform.
- C) Später.

**Antwort:** **C, 17.10.2026 — nach dem MVP.** Bis dahin Checklisten und Musterschreiben.

### Q7. Wann geht die Juristenliste raus?

J1–J13 aus Runde 1 und J14–J17 unten.

**Optionen:**
- A) Jetzt, parallel zum weiteren Bau.
- B) Gesammelt kurz vor dem Start.
- C) Weiß nicht, wen.

**Antwort:** **B, 17.10.2026.** Gesammelt kurz vor dem Start.

---

## J (Fortsetzung). Neue Fragen für den Juristen

### J14. Inserate aus dem ATS der Firma (L)

- **Frage:** Reicht der Auftrag der Firma, um ihre Inserate über die Schnittstelle
  ihres ATS-Anbieters abzurufen, oder verbieten dessen Bedingungen die
  Weiterverwendung auf einer anderen Plattform?

### J15. Persönliche Seiten (M)

- **Frage:** Was muss eine personalisierte Seite mit dem Namen einer Person
  unter der Domain einer Firma beachten (Datenschutz, wer ist Verantwortlicher —
  Firma oder Plattform)?

### J16. Import aus dem LinkedIn-Export (N1)

- **Frage:** Ist das Lesen eines Exports, den die Person selbst hochlädt, mit
  den Bedingungen von LinkedIn vereinbar, und was gilt für Daten Dritter darin
  (Kontakte)?

### J17. MCP-Zugang (N4)

- **Frage:** Wer ist verantwortlich, wenn eine Person ihre Daten über einen
  Schlüssel an einen KI-Dienst ihrer Wahl gibt — und wie weit reicht die
  Pflicht der Plattform danach?

### J18. Erfolgsgebühr als private Arbeitsvermittlung (GESCHAEFTSMODELL B/D)

- **Frage:** Ist die Plattform mit 500 € je Einstellung eine private
  Arbeitsvermittlung (§§ 296 ff. SGB III) — welcher Vertrag mit der Firma ist
  nötig, und hält eine Klausel „jede Einstellung binnen 12 Monaten nach dem
  ersten Kontakt" und „Schweigen bis Probezeitende löst die Rechnung aus"?

### J19. KI-Guthaben für Verbraucher

- **Frage:** Darf Abo-Guthaben zum Monatsende verfallen, und wie lange muss
  gekauftes Paket-Guthaben gültig bleiben? Wie lautet die Widerrufsbelehrung
  für digitale Inhalte, die sofort genutzt werden?

### J20. Empfehlungsprämien (Vitamin B, nach dem MVP)

- **Frage:** Was muss die Plattform beachten, wenn sie Privatpersonen für eine
  erfolgreiche Empfehlung bezahlt (Steuer beim Empfänger, § 22 Nr. 3 EStG;
  Meldepflichten der Plattform; ab wann ist es gewerblich)?

---

## Quellen

- [Personio: Fragen zur XML-Stellenintegration](https://support.personio.de/hc/en-us/articles/29375445597725-Frequently-asked-questions-on-XML-job-integration)
- [LinkedIn: Sign In with LinkedIn using OpenID Connect](https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2)
- [bundesAPI: jobsuche-api (keine offizielle API der Bundesagentur)](https://github.com/bundesAPI/jobsuche-api)
