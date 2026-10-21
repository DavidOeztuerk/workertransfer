# Geschäftsmodell — die Fragen, und was beantwortet ist

Begonnen am **20.10.2026**, als letzter Punkt vor dem Start (VISION-FRAGEN-2
Q1). Die Grundlage — Kosten, Folgen im Code, Rechtsrahmen — steht in
[`ENTSCHEIDUNG-VOR-DEM-START.md`](ENTSCHEIDUNG-VOR-DEM-START.md). Jede Frage
nennt einen **Vorschlag** und die Möglichkeit „nach dem MVP". Die Antworten
werden hier datiert eingetragen; daraus folgt die Bauliste.

**Was schon feststeht und nicht neu verhandelt wird:**

- Von Arbeitsuchenden darf eine private Arbeitsvermittlung höchstens 2.000 €
  nehmen, und nur bei Erfolg (§ 296 SGB III). Von Firmen ist der Preis frei.
- Der Berater arbeitet **für die Person** (ADR-0049). Verdient die Plattform
  an der Person, arbeitet er gegen ihr Interesse.
- Die Plattform **führt kein Geld** zwischen Firmen (ADR-0050): eine Ablöse
  wird festgehalten, nicht überwiesen.
- Ein Anschreiben darf **nie mitten im Schreiben** am leeren KI-Kontingent
  scheitern (Idee vom 17.10.2026).

---

## Block A — die Grundrichtung

### A1. Wer zahlt?

- **Nur Firmen; Personen zahlen nie (Vorschlag).**
- Firmen, dazu ein freiwilliges Plus für Personen (z. B. mehr KI).
- Firmen, dazu die Arbeitsagentur über den Vermittlungsgutschein (AVGS) —
  braucht eine Trägerzulassung nach AZAV (kostet selbst Geld und Zeit).
- Nach dem MVP: zum Start zahlt niemand.

**Antwort (20.10.2026):** **Für die Plattform zahlt nur die Firma, und nur
bei erfolgreicher Einstellung** — wie bei einer Personalvermittlung. Personen
und Firmen zahlen außerdem für **KI-Nutzung**; sonst ist alles kostenlos.
Später denkbar: **„Vitamin B"** — Empfehlungen, die honoriert werden (nach
dem MVP, siehe unten).

### A2. Ab wann wird bezahlt?

- **Kostenlose Startphase für Firmen (z. B. 3–6 Monate), Preise vorher
  angekündigt (Vorschlag)** — ein Marktplatz ohne Personen ist für Firmen
  nichts wert, und Personen kommen erst, wenn es Stellen gibt.
- Ab dem ersten Tag bezahlt.
- Dauerhaft kostenloser Einstieg, bezahlt werden nur Zusatzfunktionen.
- Nach dem MVP.

**Antwort (20.10.2026):** Die Plattform bleibt kostenlos. Bezahlt wird nur
die KI — mit eigenem Schlüssel (Anthropic, OpenAI, später z. B. Gemini) oder
mit einem Kontingent, das man bei uns kauft.

### A3. Wofür zahlt eine Firma?

- **Abo mit kostenlosem Einstieg (Vorschlag):** Stellen ausschreiben und
  Bewerbungen bearbeiten kostenlos; Scout, Ansprache, Wechsel, mehrere
  Mitglieder und KI im Abo.
- Pro Stellenanzeige (Laufzeit, z. B. 30 Tage).
- Erfolgsgebühr pro Einstellung — braucht ein verlässliches „eingestellt",
  und das setzt heute die Firma selbst (Anreiz, es nicht zu setzen).
- Nach dem MVP.

**Antwort (20.10.2026):** **Erfolgreiche Einstellung und KI-Nutzung.**
Kein Abo für Plattformfunktionen.

### A4. Wer trägt die KI-Kosten?

Geschätzt wenige Cent bis gut zwei Euro je aktiver Person und Monat, je nach
Modellklasse (`ENTSCHEIDUNG-VOR-DEM-START.md`).

- **Jeder Firmentarif enthält ein Kontingent; Personen bekommen ein
  kostenloses Kontingent, das die Firmen mittragen; wer mehr will, hinterlegt
  einen eigenen Schlüssel (Vorschlag).**
- KI nur mit eigenem Schlüssel — die Plattform zahlt nichts.
- Kontingente zum Zukaufen, für Personen und Firmen.
- Nach dem MVP.

**Antwort (20.10.2026):** **Personen zahlen für KI, Firmen auch; ohne
Kontingent oder eigenen Schlüssel keine KI-Generierung.** Ein Elektriker legt
kein OpenAI-Konto an — er braucht ein **Kontingent bei uns**, so günstig wie
möglich. Firmen haben oft schon einen Schlüssel oder einen **eigenen
KI-Server** — das muss eintragbar sein, samt Adresse. Ollama im Betrieb nicht
für normale Nutzer: „normale User wollen einfach nur anwenden".

### Was aus Block A folgt — geprüft am 20.10.2026

**Im Code, zwei Funde, die zum Modell nicht passen** (beide kommen auf die
Bauliste, ohne Rückfrage — sie setzen nur um, was oben steht):

1. **Unterlagen gehen nur teilweise automatisch mit.** Der Lebenslauf wird
   jeder Bewerbung fest beigelegt (`DraftPage.tsx`, Effekt mit `cvIds`);
   **Zeugnisse und Zertifikate sind je Bewerbung abgewählt** und müssen jedes
   Mal angehakt werden. Künftig: alle hochgeladenen Unterlagen sind
   vorausgewählt, abwählbar je Bewerbung.
2. **Jede Bewerbung startet sofort die KI** (`startDrafts.ts` ruft nach dem
   Anlegen `writeDraft`). Ohne Zugang scheitert das, und erst dann erscheint
   „Selbst schreiben". Mit bezahlter KI verbrauchte das **ungefragt
   Kontingent**. Künftig: Eine Bewerbung beginnt leer — „Mit KI schreiben
   (verbraucht 1)" oder „Selbst schreiben".

**Ollama:** Personen können schon heute nur Anthropic oder OpenAI mit
eigenem Schlüssel wählen; die Adresse setzt die Plattform, ein URL-Feld gibt
es für sie nicht. Ollama ist nur das Plattform-Modell auf dem Rechner des
Betreibers bis zum Start (ADR-0051).

**Wie die großen Anbieter Abos an Limits knüpfen** (Drittquellen, Okt. 2026):

- **Claude Pro/Max:** keine veröffentlichte Token- oder Nachrichtenzahl —
  ein rollierendes **5-Stunden-Fenster** plus ein **Wochenlimit**, was zuerst
  leer ist, stoppt. Max = 5× bzw. 20× Pro je Fenster. Eine Messung an 474
  Pro-Konten schätzt **≈ 25,7 Mio. Tokens pro Woche** — für ≈ 18–20 € im
  Monat. Das trägt sich nur, weil die meisten Nutzer ihr Limit nie erreichen.
- **ChatGPT:** Go ≈ 7,99 €, Plus ≈ 22,99 € im Monat (App Store DE).
  Alltagschat gilt als unbegrenzt; begrenzt sind aufwendige Aufgaben je
  5 Stunden.

**Was das für uns heißt:** Unsere Last ist winzig dagegen — ein Anschreiben
ist **ein** Aufruf mit ≈ 3.000 Tokens rein, ≈ 2.000 raus. Geschätzt:
≈ 3 Cent (Sonnet-Klasse), ≈ 1,5 Cent (Haiku-Klasse), ≈ 0,2 Cent (kleine
Modelle). Eine aktive Person (20 Anschreiben, 30 Antworten im Monat) kostet
≈ 0,10–1,20 $. Ein **Zeitfenster wie bei Claude braucht es nicht**; ein
Kontingent in Einheiten, die ein Mensch versteht („noch 23 Anschreiben"),
reicht — Tokens sieht nur die Abrechnung.

**Zahlungskosten** (Drittquellen): Stripe EU-Karte ≈ 1,5 % + 0,25 €, SEPA
≈ 0,35 €; PayPal direkt ≈ 2,49 % + 0,35 €. Bei 2,99 € gehen damit ≈ 0,30 €
an den Zahlungsweg — Kleinstbeträge unter ≈ 3 € lohnen nicht.

**Vitamin B — braucht ein Empfehlender eine Selbstständigkeit?**
Eine Prämie vom **eigenen Arbeitgeber** ist Lohn (Lohnsteuer, keine
Selbstständigkeit). Eine Prämie **der Plattform an eine Privatperson** ist bei
Gelegenheit „sonstige Einkunft" (§ 22 Nr. 3 EStG) mit einer **Freigrenze von
256 € im Jahr** — darüber ist der ganze Betrag steuerpflichtig; wer
regelmäßig und mit Gewinnabsicht empfiehlt, ist gewerblich. Keine
Steuerberatung — Punkt für die Anwalts-/Steuerliste. **Consent-first:**
niemand lädt Daten über einen anderen hoch; der Empfehlende schickt einen
Link, die empfohlene Person registriert sich selbst und bestätigt selbst, wer
sie empfohlen hat. Auszahlungen an Privatpersonen brauchen eine
Identitätsprüfung (z. B. Stripe Connect). **Nach dem MVP.**

---

## Block B — Erfolg und KI-Zugang

**B1. Erfolgsgebühr — Antwort (21.10.2026):** **Pauschale von 500 € netto je
Einstellung**, gleich für jede Stelle. Ein abgeschlossener Wechsel zählt wie
eine Einstellung. Keine Ablöse und kein Gehalt fließen in die Rechnung ein.

**B2. Beleg und Fälligkeit — Antwort (21.10.2026):** **Firma und Person
melden beide** die Einstellung; die **Rechnung ist erst nach der Probezeit
fällig**. Die **Probezeit wird angegeben**, weil sie verhandelbar ist und
von Vertrag zu Vertrag anders sein kann. Geht die Person in der Probezeit,
fällt keine Gebühr an.

**B3. KI für Personen — Antwort (21.10.2026):** **Pakete und Abo** — aber
anders gepackt, weil es **mehrere KI-Agenten** gibt, die verschieden viel
kosten (Block C1).

**B4. Firmen mit eigenem KI-Zugang — Antwort (21.10.2026):** **Schlüssel
(Anthropic/OpenAI) und eigener Server**; ein eigener Server nur unter der
bestätigten Firmendomain, nur https, nur öffentliche Adresse.

**Noch offen aus B:** Gebühr für einen abgeschlossenen Wechsel mit Ablöse
ist mit B1 beantwortet (500 € wie jede Einstellung). Vermittlungsgutschein
(AVGS) und Zahlungsausfall folgen in einer späteren Runde.

### Die KI-Agenten, und was sie ungefähr kosten

| Seite | Agent | Last je Aufruf |
|---|---|---|
| Person | Anschreiben schreiben | groß (Stelle + Lebenslauf + Profil rein, ein Brief raus) |
| Person | Anschreiben überarbeiten (Kommentare) | groß |
| Person | KI-Berater im Gespräch | mittel (Gespräch + Mandat + Frist) |
| Person | Antwortentwurf in Nachrichten | klein (letzte Nachrichten) |
| Person | Profilhilfe | klein |
| Firma | Stellenanzeige formulieren | mittel |
| Firma | Ansprache aus dem Scout | klein |
| Firma | Antwortentwurf in Nachrichten | klein |

## Block C — wie die KI verkauft wird

**C1 — Antwort (21.10.2026):** **Ein Guthaben, fester Preis je Agent.** Man
kauft „KI-Guthaben"; jeder Agent kostet sichtbar einen festen Betrag (z. B.
Anschreiben 3, Überarbeiten 2, Berater 2, Antwort 1, Profilhilfe 1). Die
Anzeige sagt „reicht für ca. 30 Anschreiben". Die Gewichte werden an den
gemessenen Tokens je Agent ausgerichtet, nicht geraten.

**C2 — Antwort (21.10.2026):** **Ja, ein kleines Startguthaben, einmalig**,
für Personen und Firmen (z. B. genug für 3 Anschreiben).

**C3 — Antwort (21.10.2026):** **Paket-Guthaben verfällt nie, Abo-Guthaben
zum Monatsende**; Paket-Guthaben wird erst verbraucht, wenn das Abo-Guthaben
leer ist. Anwalt prüft die Verfallsregel.

**C4 — Antwort (21.10.2026):** **Stripe** — Karte, SEPA, Apple/Google Pay,
Abo, Pakete, Kundenportal, Rechnungen für Firmen, Steuer über Stripe Tax.

### Die Rechnung hinter den Preisen (geschätzt, 21.10.2026)

Plattform-Modell in der Sonnet-Klasse (≈ 2 $ / 10 $ je Mio. Tokens):

| Agent | Tokens rein / raus (geschätzt) | Kosten | Gewicht |
|---|---|---|---|
| Anschreiben | 3.000 / 2.000 | ≈ 2,6 Cent | 3 |
| Überarbeiten | 3.500 / 2.000 | ≈ 2,7 Cent | 2–3 |
| Berater | 2.500 / 1.500 | ≈ 2,0 Cent | 2 |
| Antwortentwurf | 1.500 / 1.000 | ≈ 1,3 Cent | 1 |
| Profilhilfe | 800 / 800 | ≈ 1,0 Cent | 1 |

**Erste Messung (21.10.2026)**, lokales Modell `qwen2.5-coder:7b`, die knappen
Testdaten von `make ai-check`: **Anschreiben 691 Tokens rein / 250 raus,
Profilhilfe 192 / 11.** Die Schätzung oben liegt deutlich darüber — gewollt
vorsichtig. Mit echten Lebensläufen steigt die Eingabe, und ein Modell, das vor
dem Schreiben nachdenkt (Anthropic, `effort: low`), verbraucht mehr Ausgabe.
**Vor der Preisseite** wird mit dem Anbieter im Betrieb und echten Profilen neu
gemessen: `docker logs <dienst> | grep "Model call"`.

Ein Guthaben-Punkt kostet uns damit **≈ 1–1,3 Cent**. Verkauft zu
**≈ 2,5–3 Cent** (brutto) bleibt nach 19 % USt und Zahlungsgebühr auch dann
etwas übrig, wenn jemand sein ganzes Guthaben in die teuerste Aktion steckt.
**Messen, bevor ein Preis veröffentlicht wird:** `ModelWire` muss die echten
Tokens je Aufruf (`usage`) lesen und je Agent mitschreiben — dann ersetzen
Messwerte diese Tabelle.

---

## Block D — Preise und Probezeit

**D1 — Antwort (21.10.2026):** Personen, brutto: **Paket 100 Punkte für
2,99 €** (verfällt nie) und **Abo 4,99 € im Monat mit 200 Punkten**.
Startguthaben **10 Punkte** (≈ 3 Anschreiben).

**D2 — Antwort (21.10.2026):** Firmen, netto: **Paket 500 Punkte für
9,90 €** und **Abo 19 € im Monat mit 1.200 Punkten**.

**D3 — Antwort (21.10.2026):** Die Firma trägt bei „eingestellt" **Start und
Probezeit** ein, die Person bestätigt. Kurz vor dem Ende fragt die Plattform
beide. **Meldet keiner ein vorzeitiges Ende, geht die Rechnung über 500 €
raus**; die Meldepflicht steht in den AGB. Endet das Arbeitsverhältnis in der
Probezeit, fällt keine Gebühr an.

**D4 — Antwort (21.10.2026):** Es zählt **jede Einstellung innerhalb von
12 Monaten nach dem ersten Kontakt über die Plattform** — Bewerbung,
Ansprache, Initiativbewerbung oder Wechsel. Anwalt prüft.

---

## Das Modell in fünf Sätzen (Stand 21.10.2026)

1. Die Plattform ist für Personen und Firmen **kostenlos**.
2. Eine Firma zahlt **500 € netto je Einstellung** über die Plattform,
   **fällig nach der Probezeit**, und nur, wenn niemand ein vorzeitiges Ende
   gemeldet hat.
3. **KI kostet für alle** — mit eigenem Schlüssel (Firmen auch mit eigenem
   Server) oder mit **KI-Guthaben** bei uns; ohne beides keine Generierung,
   aber alles andere geht, auch das Anschreiben von Hand.
4. KI-Guthaben ist **ein Saldo in Punkten**, jeder Agent hat einen festen
   Preis; Personen 2,99 € / 4,99 €, Firmen 9,90 € / 19 € netto; ein kleines
   Startguthaben für alle.
5. Bezahlt wird über **Stripe**.

## Bauliste daraus

| # | Was | Größe | Vor dem Start? |
|---|---|---|---|
| 1 | ~~**Unterlagen vorausgewählt**: alle hochgeladenen Unterlagen gehen standardmäßig mit, abwählbar je Bewerbung~~ | S | **gebaut 21.10.2026** |
| 2 | ~~**KI nicht mehr automatisch**: eine Bewerbung beginnt leer — „Mit KI schreiben" oder „Selbst schreiben"; auch die Sammelbewerbung fragt vorher~~ | S–M | **gebaut 21.10.2026** — Zustand `open` in applications-service; der Hinweis „verbraucht 3 Punkte" kommt mit dem Guthaben (#4) |
| 3 | ~~**Tokens messen**: `ModelWire` liest `usage`, je Agent mitgeschrieben (Zahlen, nie Inhalt)~~ | S | **gebaut 21.10.2026** — eine Logzeile je Aufruf: `Model call <agent> via <anbieter>: N tokens in, M tokens out` |
| 4 | **KI-Guthaben**: Saldo je Konto (Person oder Firma), Reservierung beim Start eines Aufrufs und Abrechnung am Ende, feste Preise je Agent, Startguthaben, Verfall (Paket nie, Abo zum Monatsende); eigener Schlüssel verbraucht nichts | L | ja |
| 5 | **Stripe**: Pakete (Checkout), Abo (Billing), Kundenportal, Webhooks → Guthaben; Stripe Tax; Rechnungen für Firmen mit USt-IdNr. | L | ja |
| 6 | **Firmen-KI-Zugang**: ~~Schlüssel (Anthropic/OpenAI)~~ und eigener Server — nur unter der bestätigten Firmendomain, https, öffentliche Adresse; Egress über `IEgressPolicy` wie bei ADR-0052 | M | **Schlüssel gebaut 21.10.2026** (`/company/settings`, ADR-0051 Ergänzung); der eigene Server steht noch aus |
| 7 | **Plattform-Modell im Betrieb**: ein gehosteter Anbieter statt Ollama; Ollama bleibt Entwicklung | S | ja |
| 8 | **Erfolgsgebühr**: Einstellung mit Start und Probezeit (Firma trägt ein, Person bestätigt), Erinnerung vor dem Ende, Rechnung über Stripe; Wechsel zählen; 12-Monats-Fenster ab erstem Kontakt | L | ja |
| 9 | **Preisseite, AGB, Widerrufsbelehrung** für digitale Inhalte; Preise in der Oberfläche | M | ja, mit dem Anwalt |
| — | Vitamin B (Empfehlungen), Vermittlungsgutschein (AVGS/AZAV), weitere KI-Anbieter (Gemini …) | — | nach dem MVP |

Dazu ein **ADR „Geld"**: was die Plattform berechnet, was nie (Personen für
Plattformfunktionen, Prozente einer Ablöse), wo Guthaben und Rechnungen
leben, und dass ein Anschreiben nie mitten im Schreiben am Guthaben scheitert.

