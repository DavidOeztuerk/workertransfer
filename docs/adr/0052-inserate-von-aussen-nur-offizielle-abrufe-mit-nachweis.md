# ADR-0052 — Inserate von außen: nur offizielle Abrufe, das Board beweist sich mit einem Code

**Datum:** 17.10.2026, korrigiert 18.10.2026 · **Status:** angenommen
**Ergänzt:** ADR-0004 (kein Scraping) um den erlaubten Weg · **Berührt:**
ADR-0019 (Domain der Firma), `VISION-FRAGEN-2.md` L1–L5, Noelias
Egress-Grenze

## Zusammenhang

Der ursprüngliche Auftrag will, dass eine Firma ihre Karriereseite verbindet und
WorkerTransfer ihre aktiven Stellen holt — aber nie über Scraping, nie gegen
Nutzungsbedingungen. Runde 2 hat entschieden: offizielle Abrufe der
Bewerbermanagement-Systeme (ATS) und ein eigener Feed, täglich und auf Klick,
pro Quelle als Entwurf oder direkt veröffentlicht, Bewerbungen über die
Plattform.

**Gemessen am 17.10.2026**, ohne Anmeldung, je eine öffentliche Firma:
Greenhouse (`boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true`,
JSON, Beschreibung HTML-escaped), Lever (`api.lever.co/v0/postings/{site}?mode=json`,
JSON mit `descriptionPlain` und `workplaceType`), Personio
(`{firma}.jobs.personio.de/xml`, XML; die Beschreibungen können leer sein).
Die Jobbörse der Arbeitsagentur bietet keine offizielle Schnittstelle
(bundesAPI) und fällt deshalb heraus.

## Entscheidung

**Vier Arten von Quellen im MVP:** Greenhouse, Lever, Personio — und ein
**eigener Feed** im Format, das Firmen für Google for Jobs ohnehin pflegen:
eine JSON-Liste von schema.org-`JobPosting`. Weitere ATS kommen einzeln dazu,
jedes erst gegen seine echte Schnittstelle gemessen.

### Das Board beweist sich selbst — mit einem Code

Jedes Board ist öffentlich lesbar, und seinen Namen kann jeder eintippen. Ohne
Bindung zeigt eine fremde Firma Acmes Stellen unter eigenem Namen, und wer sich
darauf bewirbt, gibt sein Profil der falschen Firma frei.

**Die Bindung muss vom Board ausgehen, nicht von der Firma.** Beweisen kann sie
nur eine Änderung, die allein der Inhaber des Boards machen kann:

- Die Plattform gibt beim Einrichten einen **Code** aus (`wt-` und zehn Zeichen,
  aus einer kryptografischen Quelle, ohne verwechselbare Zeichen).
- Die Firma schreibt ihn in **ein beliebiges Inserat** in ihrem ATS, in Titel
  oder Text, und klickt „Code prüfen“.
- Der Abruf, der ihn findet, bindet das Board und übernimmt im selben Schritt.
  **Bis dahin wird nichts übernommen**, und der tägliche Lauf fasst ein
  unbewiesenes Board nicht an. Danach darf der Code wieder weg: ein Board
  wechselt nicht den Besitzer.
- Der Code gehört zur Quelle, nicht zum Board. Richten zwei Firmen dasselbe
  Board ein, bekommt jede ihren eigenen, und nur der, der im Board steht,
  bindet.

Ein **eigener Feed** braucht keinen Code: er muss auf der bewiesenen Domain der
Firma liegen (ADR-0019, `https`, die Domain oder darunter), und das ist der
Beweis.

**Korrigiert am 18.10.2026 — zweimal derselbe Fehler, offen festgehalten.**
Runde 2 hatte erst einen DNS-Eintrag, dann einen Link auf der eigenen Seite
beschlossen. Beides stammte aus dem Vorschlag, und beides beweist nichts:
DNS-Eintrag und Seite kontrolliert die Firma, nicht das Board — Firma B kann
genauso gut auf Acmes Board verlinken. Beim Schreiben der Tests aufgefallen,
bevor etwas davon lief.

Gemessen am 18.10.2026, ob die Boards von sich aus auf die Domain der Firma
zeigen (dann ginge es ohne Code):

| ATS | was auf die Firma zeigt | gemessen |
|---|---|---|
| Greenhouse | `absolute_url` der Inserate, wenn die Firma das Board einbettet | Airbnb und Stripe ja, GitLab nein |
| Lever | das Logo auf der gehosteten Seite verlinkt eine Adresse, die die Firma einstellt | Spotify → `spotifyjobs.com`, Palantir → die Lever-Seite selbst |
| Personio | Impressum-Link auf der gehosteten Seite, wenn eingestellt | tado → `tado.com` |

Das trägt nicht allgemein, und für Lever und Personio müsste die Plattform deren
HTML-Seiten lesen. Der Code geht überall und nur über die offizielle
Schnittstelle. Die automatische Bindung für Greenhouse-Einbetter ist eine
spätere Bequemlichkeit (unten).

### Die Egress-Grenze erfährt, was sie durchlassen soll

Noelias Regel ist beim Start fest. Die ATS-Adressen stehen in der Konfiguration
(`Sources__Greenhouse`, `Sources__Lever`) und werden daraus abgeleitet wie jede
andere. jobs-service legt über Noelias `IEgressPolicy` eine Regel, die
zusätzlich genau zwei Dinge erlaubt: Hosts unter Personios Domain
(`Sources__Personio`, jede Firma hat dort einen eigenen) und die bewiesenen
Domains der Firmen mit einem **Feed**, nur über `https`. Beides steht im
Bericht der Grenze.

**Eine Firmen-Domain darf nicht nach innen zeigen.** Wer seine Domain auf
`10.0.0.5` auflöst, käme sonst über den Feed an die Dienste im Cluster. Der
Client für Firmen-Seiten prüft die aufgelöste Adresse beim Verbindungsaufbau
und verweigert Loopback, private Netze, Link-Local und Carrier-Grade-NAT;
Weiterleitungen folgt er von Hand, höchstens drei, und nur auf derselben Domain.

### Geholte Inserate spiegeln die Quelle

Sie tragen ihre Herkunft (Quelle, Kennung, Adresse) und werden bei jedem Abruf
angeglichen. **Der Text gehört der Quelle**: Titel, Beschreibung, Ort,
Arbeitsform und Beschäftigung werden dort geändert, hier antwortet eine Änderung
mit 409. **Was ein ATS nicht einheitlich mitbringt** — Fähigkeiten, Gehalt,
Postleitzahl — ergänzt die Firma hier, sonst bekäme das Inserat nie ein Häkchen
im Abgleich.

Verschwindet ein Inserat an der Quelle, wird ein Entwurf entfernt und ein
veröffentlichtes **geschlossen**; Bewerbungen darauf bleiben. Geschlossen bleibt
geschlossen, auch wenn es wieder auftaucht. Ein Abruf, der scheitert, ändert
kein Inserat: ein Board, das heute nicht antwortet, hat seine Stellen nicht
abgehängt. Von mehr als 500 Inseraten je Quelle werden die ersten 500
übernommen, und dann wird nichts geschlossen — die Liste ist abgeschnitten,
nicht leer.

Als Link übernommen wird nur eine absolute `https`-Adresse; ein Feed bestimmt,
was er sagt, und ein `javascript:`-Link darf auf der Seite keiner werden. Wer
eine Quelle entfernt, behält ihre Inserate als eigene.

### Abruf und Rechte

**Abgerufen wird täglich und auf Klick**, nie über Personen: ein Taktgeber
fragt stündlich, welche bewiesene Quelle älter als einen Tag ist. Lesen ist
jedes Mitglieds; einrichten, umstellen, abrufen und entfernen sind
`admin`-Schritte — eine Quelle im Modus „direkt“ veröffentlicht im Namen der
Firma, ohne dass jemand „Veröffentlichen“ drückt.

## Was später kommt

- **Ein echter Login beim ATS** statt des Codes: Greenhouse Harvest
  (API-Schlüssel), Lever (OAuth-App, von Lever freizugeben), Personio
  (API-Zugangsdaten). Bindet ohne Umweg und öffnet den Rückweg ins ATS (N3).
  Je ATS gegen ein echtes Konto zu messen, bevor gebaut wird.
- **Automatische Bindung**, wo das Board selbst auf die bewiesene Domain zeigt
  (Greenhouse-Einbettung, siehe Messung).
- **Weitere ATS**, jedes erst gemessen — die Liste steht in
  `docs/DRITTANBIETER.md`.

## Was es kostet

- jobs-service ruft drei ATS-Hosts und die Domains der Firmen mit Feed.
- Die Herkunft eines Inserats sind drei Spalten mehr; die Quellen eine Tabelle
  (`job_sources`, mit Code und Zeitpunkt der Bindung).
- Eine Firma muss einmal einen Code in ein Inserat schreiben.
- Die Nutzungsbedingungen der ATS-Anbieter prüft der Jurist (J14).
