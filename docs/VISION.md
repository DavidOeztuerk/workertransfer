# Vision: was WorkerTransfer ist

Stand 07.10.2026. Zusammengefasst aus den Antworten in
[`VISION-FRAGEN.md`](VISION-FRAGEN.md) (06.10.2026) und dem ursprünglichen
Auftrag in [`vision/kon.txt`](vision/kon.txt), Zeile 896. Wo die beiden sich
widersprechen, gilt der Katalog. Was davon gebaut ist und was fehlt, steht in
[`BAULISTE.md`](BAULISTE.md).

## In einem Satz

Ein Transfermarkt für Arbeit nach dem Vorbild des Fußballs — aber die Person
gehört niemandem: Firmen suchen und sprechen an, Personen bewerben sich oder
lassen sich finden, ein KI-Berater arbeitet nur für die Person, und gewechselt
wird nur, wenn sie es will.

## Die Rollen

| Rolle | arbeitet für | tut |
|---|---|---|
| **Person** (Arbeitnehmer) | sich selbst | Profil, Lebenslauf, Freigaben, Bewerbung, Initiativbewerbung, Marktstatus mit eigenem Zeitfenster |
| **KI-Berater** | **nur die Person**, auf ihren Wunsch | findet Firmen, berät zu Angeboten und Gehalt, spricht und verhandelt mit der neuen Firma, erklärt Verträge, bei gutem Grund auch mit dem alten Arbeitgeber |
| **Neue Firma**: Recruiter (Mensch) | die Firma | schreibt Personen an, führt Gespräche |
| **Neue Firma**: KI-Scout | die Firma | listet pro aktuellem Inserat passende Personen, nach festen Regeln, mit Häkchen, ohne Wertung und Rangfolge |
| **Neue Firma**: KI-Recruiter | die Firma | entwirft Nachrichten an Personen |
| **Alter Arbeitgeber** | sich selbst | nur wenn er auf der Plattform ist und Anfragen zulässt: Freigabe, Ablöse, Zeugnis |

Ein Agent für beide Seiten gibt es nicht (Interessenkonflikt).

## Die Grundregeln

1. **Kein Agent schickt selbst ab.** Alles, was ein Agent nach außen gibt, prüft
   und gibt vorher ein Mensch frei — die Person beim Berater, die Firma beim
   Recruiter. Wie beim Anschreiben heute.
2. **Keine Zahl über einen Menschen.** Kein Score, keine Rangliste, kein
   Ablösepreis-Vorschlag. Häkchen und Belege, die man nachprüfen kann (ADR-0022).
3. **Bewusst unter der Hochrisiko-Schwelle des EU AI Act.** Der Scout gleicht
   nach festen Regeln ab; Sprachmodelle schreiben nur Entwürfe, die ein Mensch
   freigibt. Vor dem Start prüft ein Jurist diese Einordnung.
4. **Sichtbar ist nur, was die Person freigibt**, und ein Widerruf wirkt sofort
   (ADR-0013, ADR-0020).
5. **Die Plattform bewegt kein Geld.** Beträge werden festgehalten, gezahlt wird
   außerhalb. Treuhand vielleicht später, dann über einen lizenzierten Anbieter.
6. **Unterschrieben wird außerhalb** (DocuSign oder vor Ort).
7. **Die Plattform berät nicht rechtlich.** Erklärungen, Checklisten, „Guck dir
   das genau an“ — kein Urteil über einen konkreten Vertrag (§ 2 RDG).

## Der Ablauf

### Finden

- Die Firma inseriert (mit **Gehaltsspanne**). Der **KI-Scout** zeigt pro
  Inserat, wer passt — nur Personen mit Freigabe und offenem Zeitfenster.
- Die Person bewirbt sich auf ein Inserat oder **initiativ** bei einer Firma;
  der KI-Berater bereitet vor, sie gibt frei.

### Sprechen

- **Nachrichten auf der Plattform, ein Gespräch pro Vorgang.** Dort schreiben
  auch die Agenten, dort wird freigegeben. Der Berater liest nur auf der Seite
  der Person mit, sichtbar und abschaltbar.
- Termine kommen in einer späteren Version.

### Verhandeln

- **Person (oder Berater) ↔ neue Firma:** Gehalt, Start, Bedingungen. Der
  Berater kennt die Kündigungsfrist der Person und warnt, wenn ein Angebot vor
  dem frühesten Wechsel beginnt.
- **Neue Firma ↔ alter Arbeitgeber:** nur, wenn der alte Arbeitgeber auf der
  Plattform ist, Anfragen zu seinem Personal zulässt **und** die Person
  zustimmt. Dann geht es um die **Ablöse**: den Preis dafür, dass der alte
  Arbeitgeber die Person vor Ende der Kündigungsfrist gehen lässt. Beide Firmen
  sehen die Restzeit, die Plattform nennt keinen Preis. Die Person muss dem
  Ergebnis zustimmen und kann jederzeit ablehnen.
- **Person (oder Berater) ↔ alter Arbeitgeber:** nur bei gutem Grund — früher
  gehen, Ablöse, ausdrücklicher Wunsch, Zeugnis.
- Ist der alte Arbeitgeber **nicht** auf der Plattform, gibt es keinen
  Transfer: die Person kündigt oder schließt selbst einen Aufhebungsvertrag und
  bestätigt „ich bin frei“.

### Abschließen

- Den Vertrag schreibt die Firma selbst **oder** sie nimmt eine geprüfte Vorlage
  der Plattform, die automatisch mit den vereinbarten Daten gefüllt wird.
- Wer keinen Berater hat, bekommt Checklisten und den Hinweis, einen Vertrag vor
  dem Unterschreiben prüfen zu lassen, mit Links zu neutralen Stellen.

## Daten und KI-Anbieter

- Standard-Anbieter für alle Agenten: **Mistral (EU)**. Die Person kann einen
  anderen wählen, auch ein lokales Modell (Ollama).
- Bekannte Felder (Name, Arbeitgeber, Gehalt, Vertragsdaten) gehen nur als
  **Platzhalter** an das Modell und werden danach wieder eingesetzt.
- Nichts davon wird beim Anbieter abgelegt.

## Zeit

- **Kein festes Transferfenster.** Jede Person kann sich ein eigenes setzen
  („offen von Januar bis März“) und jederzeit ändern.
- Die **Kündigungsfrist** bestimmt den frühesten Wechsel — und damit, wie viel
  eine Ablöse wert ist.

## Vor dem Start zu entscheiden

- Wer die Plattform bezahlt (Geschäftsmodell).
- Control Plane im Betrieb (eigener Identitätsanbieter, echte Lizenz).
- Arbeitgeber, die Personal aktiv abgeben (z. B. vor einer Insolvenz).
- Die zwölf Prüfpunkte für einen Juristen (`VISION-FRAGEN.md`, Teil J).
