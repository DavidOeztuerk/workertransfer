# ADR-0055 — Geld: KI-Guthaben, Erfolgsgebühr und Rechnungen in einem eigenen Dienst

**Datum:** 21.10.2026 · **Status:** vorgeschlagen
**Berührt:** [`GESCHAEFTSMODELL-FRAGEN.md`](../GESCHAEFTSMODELL-FRAGEN.md) (Bauliste 4, 5, 8),
ADR-0025 (Outbox), ADR-0027 (Löschung), ADR-0050 (Ablöse), ADR-0051 (KI-Zugang)

## Zusammenhang

Das Geschäftsmodell steht (20./21.10.2026): die Plattform ist kostenlos, eine
Firma zahlt **500 € netto je Einstellung, fällig nach der Probezeit**, und
**KI kostet für alle** — mit eigenem Schlüssel oder mit **KI-Guthaben** bei
uns, bezahlt über **Stripe**. Drei Dinge fehlen dafür im Baum: ein Guthaben,
das fünf Dienste belasten, ein Weg, auf dem Geld hereinkommt, und eine
Rechnung, die erst nach einer Probezeit fällig wird.

Gemessen ist bisher eine Zahl: die Tokens je Aufruf (PR #106). Ein
Anschreiben kostete am lokalen Modell 691 Tokens rein und 250 raus.

## Entscheidung 1 — ein eigener Dienst: `billing-service`

Der sechzehnte Dienst, Port **8016**, eigene Datenbank. Er hält:

- das **Guthaben** je Eigentümer — eine Person (`SubjectId`) oder eine Firma
  (`TenantId`), nie beides in einer Zeile. Wer zahlt, sagt die `Capacity` im
  Token des rufenden Dienstes, nie ein Feld, das der Browser schickt;
- die **Buchungen**: Gutschrift (Start, Paket, Abo-Monat), Reservierung,
  Abrechnung, Freigabe, Verfall;
- die **Einstellungsfälle** für die Erfolgsgebühr;
- **Verweise** auf Stripe (Kunde, Zahlung, Rechnung) — nie Kartendaten.

**Warum nicht in identity-service:** dort fällt mit der Löschung alles.
Rechnungen haben ein anderes Recht (Entscheidung 5). In identity hieße das,
dass ein Teil der Kontodaten die Löschung überlebt; in einem eigenen Dienst
überlebt nur, was das Gesetz verlangt.

**Warum nicht in den fünf KI-Diensten:** ein Saldo, den fünf Dienste führen,
sind fünf Salden.

**Rechte:** kaufen, abonnieren und kündigen für eine Firma ist **`admin`** —
es bindet die Firma an eine Zahlung. Verbrauchen darf, wer den Agenten
benutzen darf; daran ändert sich nichts.

## Entscheidung 2 — reservieren, schreiben, abrechnen

**Nur das Plattform-Modell kostet Guthaben.** Wer einen eigenen Schlüssel
hinterlegt hat, ruft billing-service gar nicht auf.

1. **Vor** dem Modellaufruf: `POST /internal/credits/reserve` `{owner, agent}`
   reserviert den **festen Preis des Agenten** (C1: Anschreiben 3,
   Überarbeiten 2–3, Berater 2, Antwort 1, Profilhilfe 1). Reicht es nicht:
   **402**, und die Oberfläche sagt „Guthaben aufladen" — bevor ein Wort
   geschrieben ist.
2. Das Modell schreibt.
3. **Danach** bucht `POST /internal/credits/settle` `{reservation, tokens_in,
   tokens_out}` den festen Preis und hält die Tokens fest — für die Messung,
   nicht für den Preis. Scheitert das Modell: `release`, nichts wird berechnet.

Damit hält die Zusage aus `GESCHAEFTSMODELL-FRAGEN.md`: ein Anschreiben
scheitert **nie mitten im Schreiben** am leeren Guthaben, denn bezahlt ist,
bevor es beginnt.

- **Der Preis steht am Knopf** („Mit KI schreiben · 3 Punkte"), der Saldo im
  Kontomenü. Wer klickt, weiß vorher, was es kostet.
- **Reihenfolge des Verbrauchs (C3):** erst Abo-Guthaben, dann Paket.
- **Startguthaben (C2/D1):** 10 Punkte, einmal, für Personen **und** Firmen —
  gutgeschrieben beim ersten Blick auf das Konto, nicht bei der Registrierung,
  damit dafür kein neuer Weg durch identity nötig ist.
- **Der Saldo wird nie negativ:** Reservierung in einer Transaktion mit
  Zeilensperre auf dem Konto.
- **Eine Reservierung verfällt** nach einer Stunde ohne Abrechnung — sonst
  friert ein abgestürzter Aufruf Guthaben ein.
- **Schweigt billing-service, wird nicht geschrieben** (503, „Guthaben gerade
  nicht prüfbar") — nicht umsonst und nicht auf Pump. Dieselbe Regel wie beim
  Ledger: Schweigen ist kein Ja.

**Ein Vertrag, ein Client.** `Contracts.Credits` trägt die drei Rümpfe,
`ServiceDefaults/Ai` den Client neben `ModelWire`. Fünf Dienste rufen ihn; fünf
eigene Kopien wären fünf Gelegenheiten, die Reservierung zu vergessen.

## Entscheidung 3 — Stripe, und nur Stripe sieht Zahlungsdaten

- **Pakete** über Stripe Checkout (Einmalzahlung), **Abos** über Stripe
  Billing, **kündigen und Rechnungen** im Stripe-Kundenportal.
- **Gutgeschrieben wird auf den Webhook**, nie auf die Rückkehr des Browsers:
  `checkout.session.completed` (Paket), `invoice.paid` (Abo-Monat). Die
  Signatur wird geprüft, jedes Ereignis genau einmal gebucht (Ereignis-Id).
  Der Webhook ist die einzige Route ohne Token, und die Signatur ersetzt ihn.
- **Preise sind Stripe-Price-Ids in der Konfiguration**, nie Zahlen im Code.
- **`api.stripe.com` steht als `Stripe__BaseUrl` in der Konfiguration**, und
  die Egress-Grenze leitet den Host daraus her wie jeden anderen.
- **Steuer** über Stripe Tax: Personen brutto, Firmen netto mit USt-IdNr.

## Entscheidung 4 — die Erfolgsgebühr

- **Ein Einstellungsfall entsteht am Vorgang**: eine Bewerbung auf `hired`,
  ein abgeschlossener Wechsel, oder ein „Eingestellt" im Gespräch einer
  Ansprache oder Initiativbewerbung. Der Dienst des Vorgangs meldet ihn über
  die Outbox — inhaltsfrei: Firma, Person, Vorgang, Datum des ersten Kontakts
  (ADR-0025).
- **Kein Register, wer wen getroffen hat.** Der erste Kontakt steht schon im
  Vorgang (Bewerbung, Gespräch, Wechsel) und wird beim Melden von dort
  gelesen. Eine Tabelle in billing-service, die jede Begegnung von Firma und
  Person mitschreibt, wäre genau das Verzeichnis, das scout-service ablehnt.
  Das 12-Monats-Fenster (D4) ist eine Pflicht der Firma in den AGB, kein
  Abgleich der Plattform.
- **Die Firma trägt Start und Probezeit ein, die Person bestätigt (D3).** Kurz
  vor dem Ende fragt die Plattform beide. **Meldet keiner ein vorzeitiges
  Ende, geht die Rechnung über 500 € netto raus** (Stripe Invoicing,
  Zahlungsziel 14 Tage). Endet es in der Probezeit, entfällt sie.
- **Die Rechnung nennt die Person nicht** — eine Fallnummer und das
  Startdatum. Die Firma weiß, wen sie eingestellt hat; eine Rechnung liegt in
  einer Buchhaltung, die mehr Menschen lesen als die Personalakte.
- **Die Plattform führt weiter kein Geld zwischen Firmen** (ADR-0050): eine
  Ablöse fließt nicht in die Gebühr.
- Eine neue Benachrichtigungsart, `hiring_check`, für die Frage vor dem Ende
  der Probezeit. Sie nennt keine Firma, wie alle.

## Entscheidung 5 — Löschung und Aufbewahrung

**Das Guthaben fällt mit der Löschung**: Saldo, Reservierungen, Buchungen,
Tokens. Nichts verlangt, es zu behalten.

**Ein Einstellungsfall verliert die Person**, wenn sie löscht: `subject_id`
wird `null`, Firma, Daten und Fallnummer bleiben. Die Gebühr ist eine
Schuld der Firma, nicht eine Angabe über die Person — und ihre Bestätigung
entfällt dann, die Frage vor dem Probezeitende geht nur noch an die Firma.

**Rechnungen bleiben, so lange das Steuerrecht es verlangt** (§ 147 AO,
§ 257 HGB; Art. 17 Abs. 3 lit. b DSGVO). In billing-service bleibt **nur**:
Rechnungsnummer, Betrag, Datum, Stripe-Verweis — kein Profil, kein
Lebenslauf, keine Nachricht. Die Rechnung selbst, mit Name und Anschrift, liegt
bei Stripe als Auftragsverarbeiter.

**Das ist eine Ausnahme vom Versprechen „die Vorgabe löscht vollständig"**
(ADR-0027), und sie wird wie die anderen behandelt: eine benannte Konstante in
billing-service, ein Test, der ihren Umfang festhält, und **ein Satz auf
`/delete-account`**: „Rechnungen über gekauftes Guthaben bewahren wir auf,
solange das Steuerrecht es verlangt — nur Nummer, Betrag und Datum." Wer nie
etwas gekauft hat, ist nicht betroffen, und der Satz erscheint nur dann.

billing-service ist **Löschempfänger** (`ErasureRecipient.Services`) vom ersten
Tag an — die Kaskade hat dann dreizehn Empfänger.

## Was dieses ADR nicht entscheidet

- **Die Preise** — sie stehen in `GESCHAEFTSMODELL-FRAGEN.md` und werden vor
  der Preisseite am Anbieter im Betrieb neu gemessen.
- **Den KI-Anbieter im Betrieb** (Bauliste 7).
- **Steuerliche Einzelheiten** (Kleinunternehmer, Reverse Charge, Fristen) —
  Steuerberater.
- **Auszahlungen an Privatpersonen** (Vitamin B) — nach dem MVP.
- **Die Rechtsfragen J18 und J19** (`VISION-FRAGEN-2.md`). Hier steht, wie
  gebaut wird, solange der Anwalt nichts anderes sagt.

## Was als Test gilt

- Kein Aufruf des Plattform-Modells ohne Reservierung; ein eigener Schlüssel
  reserviert nie.
- Ein gescheiterter Aufruf gibt frei und bucht nichts.
- Zwei gleichzeitige Reservierungen über den letzten Punkt: eine geht durch,
  eine bekommt 402. Der Saldo ist danach null, nicht minus eins.
- Ein Stripe-Ereignis, zweimal zugestellt, bucht einmal; eines mit falscher
  Signatur bucht nie.
- Kaufen als `member` einer Firma: 403.
- Nach einer Löschung bleibt **genau** die Rechnungszeile und der Fall ohne
  Person, sonst nichts — gemessen in der Datenbank, nicht an der Antwort.
