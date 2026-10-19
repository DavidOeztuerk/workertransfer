# Vor dem Start — die drei offenen Entscheidungen, vorbereitet

Stand **20.10.2026**. Diese Unterlage **entscheidet nichts**. Sie legt für die
drei Fragen, die in `VISION-FRAGEN-2.md` als *vor dem Start* offen stehen
(Q1 Geschäftsmodell, Q2 Betreiber, Q3 Hosting), nebeneinander, was jede
Antwort kostet, was sie im Code auslöst und was sie ausschließt. Entscheiden
muss der Betreiber; manches davon ein Steuerberater oder Anwalt.

**Die Reihenfolge ist nicht beliebig:** Q2 zuerst — wer betreibt, steht im
Impressum, in der Datenschutzerklärung und auf jedem Vertrag mit einem
Hoster oder KI-Anbieter. Q3 danach, weil der Hoster ein Auftragsverarbeiter
dieses Betreibers wird. **Q1 zuletzt**, wie in Runde 1 vereinbart — aber
spätestens, bevor die erste Firma ein Konto anlegt, denn die Startseite und
die Registrierung versprechen heute nichts über einen Preis, und das muss so
bleiben, bis er feststeht.

Preise sind Listenpreise aus öffentlichen Quellen, netto, im Oktober 2026
gelesen — **nicht verhandelt und nicht gegengeprüft**. Vor einer Entscheidung
gehört jede Zahl auf der Preisseite des Anbieters nachgesehen; zwei der unten
genannten haben sich 2026 bereits zweimal geändert.

---

## Q2 — Wer betreibt die Plattform?

| | Einzelunternehmen | UG (haftungsbeschränkt) | GmbH |
|---|---|---|---|
| Gründung | Gewerbeanzeige, Tage | Notar + Handelsregister, Wochen | Notar + Handelsregister, Wochen |
| Kapital | keins | ab 1 € (25 % Rücklage aus Gewinn) | 25.000 € (12.500 € bei Gründung) |
| Haftung | **privat, unbeschränkt** | auf das Gesellschaftsvermögen | auf das Gesellschaftsvermögen |
| Laufende Kosten | gering | Bilanz, Jahresabschluss, Offenlegung | wie UG |

**Was die Haftungsfrage hier schwerer macht als bei einer gewöhnlichen
Website:** die Plattform hält Lebensläufe, Zeugnisse und Kündigungsfristen von
Menschen, die ihren Arbeitgeber nicht wissen lassen wollen, dass sie suchen.
Ein Datenschutzvorfall trifft bei einem Einzelunternehmen das private
Vermögen. Das ist **keine Rechtsberatung**, sondern der Grund, warum diese
Frage vor Q3 gehört und ein Steuerberater sie mitentscheiden sollte.

**Was sie im Code auslöst:** nichts — sie füllt `legal.imprint*` und
`legal.privacyContact*` (heute bewusst leer, „eine erfundene Anschrift wäre
schlimmer als eine fehlende"). Dazu die Gewerbeanzeige und, wegen der
Vermittlung, die Frage aus Q1 nach der Anzeige als private Arbeitsvermittlung.

---

## Q3 — Wo läuft sie?

**Was läuft:** fünfzehn Dienste und ein Gateway aus **einem** Image
(ADR-0028), Postgres (fünfzehn Datenbanken), Valkey, Mailversand, dazu das
Web-Image. Gemessen ≈ 2,8 GiB Arbeitsspeicher ohne Last — siehe unten, „Was der Stapel braucht".

**Was die Plattform vom Hoster verlangt — und was nicht:**

- **Sitz und Rechenzentrum in der EU**, Auftragsverarbeitungsvertrag nach
  Art. 28 DSGVO. Kein Drittlandtransfer für die Daten selbst.
- **Kein Managed-Dienst ist Pflicht.** Alles läuft heute in `docker compose`
  und im Helm-Chart (`make k8s-up`) — beides ist vorhanden.
- **Ausgehender Verkehr ist gering:** die Egress-Grenze erlaubt nur, was die
  Konfiguration nennt (Dienste untereinander, GitHub, die KI-Ziele, die
  Stellenquellen). Traffic-Pakete spielen kaum eine Rolle.
- **`replicaCount: 1`** ist eine Entscheidung (Schemawanderung beim Start,
  Bremse im Gateway zählt im Prozess). Ein Cluster mit drei Knoten kauft hier
  zunächst Verfügbarkeit, keine Last.

| Weg | Was man bekommt | Größenordnung / Monat | Was es kostet außer Geld |
|---|---|---|---|
| **A1 Hetzner Cloud, eine VM, `docker compose`** | eine Maschine, Snapshots, Firewall | geteilte vCPUs ab niedrigen zweistelligen €; dedizierte (CCX13, 2 vCPU/8 GB) nach der Juni-Erhöhung ≈ 43 € | Betrieb selbst: Updates, Backups von Postgres, TLS. Ein Ausfall der VM ist ein Ausfall der Plattform |
| **A2 Hetzner, k3s auf 2–3 VMs + Helm-Chart** | das Chart läuft, wie es ist | ≈ dreifach A1 | Clusterbetrieb selbst — das teuerste Wissen in dieser Tabelle |
| **A3 IONOS Managed Kubernetes** | Kontrollebene kostenlos, Knoten nach Stunden | Dritte rechnen ≈ 45 € für drei kleine Knoten | Postgres selbst im Cluster oder als DBaaS dazu |
| **A4 STACKIT (SKE)** | deutsche Kontrollebene, BSI-C5-Umfeld | Kontrollebene ≈ 72–75 €, kleiner 3-Knoten-Cluster ≈ 107 € | teurer; spricht Firmenkunden an, die „deutsche Cloud" verlangen |
| **B US-Anbieter, EU-Region** | volle Managed-Palette | höher | Drittlandfrage (CLOUD Act) bleibt im Gespräch — widerspricht dem, was die Plattform über Datenhoheit sagt |

**Vorschlag zur Reihenfolge, keine Entscheidung:** A1 für den Start mit
wenigen Firmen — `docker compose` ist der heute am besten gemessene Weg (CI
fährt genau ihn). Der Umzug auf A2/A3/A4 ist mit dem Chart vorbereitet und
wird nötig, sobald ein Ausfall von Stunden nicht mehr tragbar ist. **Vor dem
Start unabhängig vom Weg:** Postgres-Sicherung mit geprüfter Wiederherstellung,
Infisical für die Umgebung (`DRITTANBIETER.md`), TLS, die CSP der Oberfläche.

---

## Q1 — Geschäftsmodell, mit der KI als größtem variablen Posten

**Die harte Grenze steht schon fest:** von Arbeitsuchenden darf eine private
Arbeitsvermittlung höchstens 2.000 € nehmen (§ 296 SGB III), von Firmen ist
der Preis frei. Und die Plattform hat sich festgelegt, dass der Berater *für
die Person* arbeitet (ADR-0049) — verdient sie an der Person, arbeitet er
gegen ihr Interesse.

**Die vier Optionen aus Q1**, mit dem, was sie im Code auslösen:

| | Wer zahlt | Was gebaut werden muss |
|---|---|---|
| A Abo für Firmen | Firmen, monatlich | Zahlungsanbieter, Tarif je Firma, Sperre bei Zahlungsverzug — und die Entscheidung, *was* gesperrt wird (nie die Person, nie eine laufende Bewerbung) |
| B A + Provision bei Ablöse | Firmen, dazu je Wechsel | wie A, plus: die Plattform „führt kein Geld" (terms4, ADR-0050) — eine Provision auf die Ablöse bricht diesen Satz oder braucht eine eigene Rechnung außerhalb der Verhandlung |
| C Provision je Einstellung | Firmen, je Einstellung | ein verlässliches Signal „eingestellt" — heute setzt das die Firma selbst, also ein Anreiz, es nicht zu setzen |
| D zurückstellen | — | — |

**Die KI kostet je Aufruf, und das macht sie zum Kern der Rechnung.** Seit
ADR-0051 gilt: eigener Schlüssel, sonst das Modell der Plattform. Die Idee vom
17.10.2026 — ein Anbieter, genutzt per Abo oder Kontingent, *nie* ein
Anschreiben, das mitten im Schreiben am leeren Kontingent scheitert — braucht
eine Zahl je Aufruf. **Geschätzt, nicht gemessen:**

| Annahme je Aufruf | ≈ 3.000 Zeichen Kontext rein, ≈ 1.500 Wörter Denken + Text raus |
|---|---|
| Sonnet-Klasse (≈ 2 $ / 10 $ je Mio. Tokens) | ≈ 3–5 Cent |
| kleines Modell (Haiku-, Mistral-Small-Klasse, ≈ 0,10–0,15 $ / 0,50–0,60 $) | ≈ 0,2–0,5 Cent |
| Ollama auf eigener Hardware | Strom und eine GPU-Maschine, kein Preis je Aufruf |

Eine Person, die im Monat zwanzig Anschreiben, dreißig Antwortentwürfe und
ein paar Beratungen auslöst, kostet damit **zwischen wenigen Cent und gut
zwei Euro** — der Unterschied ist die Modellklasse, nicht die Menge.

**Was vor einer Entscheidung gemessen gehört:** die echten Tokens je Aufruf.
Beide Anbieter melden sie in der Antwort (`usage`); `ModelWire.cs` liest sie
heute nicht. Eine Woche mit einem echten Schlüssel auf `make ai-check` und den
vier Nähten ergäbe die Zahl, die diese Tabelle nur schätzt — und das gehört
gemessen, bevor ein Preis darauf steht.

**Und die Reservierung**, die die Idee verlangt: ein Kontingent wird beim
*Start* eines Aufrufs um den geschätzten Höchstbedarf belastet und am Ende
auf den tatsächlichen zurückgebucht. Das ist ein eigener kleiner Dienst mit
einer Tabelle je Konto — und es ist die Stelle, an der „nie mitten im
Schreiben scheitern" zur Mechanik wird statt zu einem Absatz.

---

## Was der Stapel braucht (gemessen)

Gemessen am **20.10.2026** am lokalen `docker compose`-Stapel, unmittelbar nach
36 E2E-Reisen und einem Route-Map-Lauf (`docker stats --no-stream`):

| | Arbeitsspeicher |
|---|---|
| ein .NET-Dienst | ≈ 80–230 MiB (Profil 82, Firmen 226) |
| Gateway | ≈ 100 MiB |
| Postgres (fünfzehn Datenbanken, Testdaten) | ≈ 105 MiB |
| Valkey, Mailpit | ≈ 45 / 55 MiB |
| **alles außer der Oberfläche** | **≈ 2,8 GiB** |
| Vite-Dev-Server | ≈ 1 GiB — **entfällt im Betrieb**, dort liefert nginx Dateien aus |

Die CPU stand im Leerlauf nahe null. Das ist ein Stand mit Testdaten und ohne
Last — **keine Lastmessung**. Daraus folgt nur eine Untergrenze: eine Maschine
mit **8 GB** trägt den Stapel mit Platz für den Cache von Postgres; mit 4 GB
bliebe kaum Reserve. Postgres wächst mit den Dokumenten nicht — deren Bytes
liegen im `DocumentStore`, heute nur `LocalDocumentStore`, also **auf der
Platte**. Daraus folgt für jeden Weg in Q3: **eine Sicherung von Postgres
allein ist keine Sicherung** — das Volume mit den Dokumenten gehört dazu, und
beides muss zum selben Zeitpunkt passen, sonst zeigt eine Zeile auf eine Datei,
die es nicht gibt.

---

Quellen der Preise (Oktober 2026, Drittquellen außer wo vermerkt):
[Hetzner CCX nach der Juni-Erhöhung](https://webhosting.today/2026/06/18/hetzners-price-increases-reached-209-the-30-headline-applied-to-a-different-tier/),
[Northflank zu Hetzner 2026](https://northflank.com/blog/hetzner-cloud-server-price-increases),
[IONOS Managed Kubernetes (Anbieter)](https://cloud.ionos.com/managed/kubernetes),
[IONOS Preisliste (Anbieter)](https://docs.ionos.com/cloud/support/general-information/price-list/ionos-cloud-inc),
[STACKIT SKE (Anbieter)](https://stackit.com/en/products/runtime/stackit-kubernetes-engine),
[eucloudcost zu STACKIT](https://www.eucloudcost.com/providers/stackit/),
[Claude-API-Preise Okt. 2026](https://dev.to/scorpion4ik26lang/claude-api-pricing-per-million-tokens-all-models-oct-2026-2am2),
[Mistral-API-Preise](https://costgoat.com/pricing/mistral-api).
