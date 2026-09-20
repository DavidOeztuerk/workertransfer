---
name: wt-nachweis
description: Use when building or reviewing anything in WorkerTransfer that maps an observation to a regulation — a Pruefung citing an article, a Nachweis document, the /nachweis pages, an ADR about AI duties, or copy that describes any of it. Holds the forbidden words, the four-field citation shape, and which duties a program can and cannot establish.
---

# Belege, keine Konformität

WorkerTransfer vermittelt Menschen in Arbeit und setzt dabei Modelle ein. Das ist
der Bereich, in dem eine zu große Behauptung teuer wird — rechtlich und beim
ersten Betriebsrat, der nachfragt.

## Die Wortliste

| Nie | Stattdessen |
|---|---|
| zertifiziert, Zertifikat, Zertifizierung *(über unser Ergebnis)* | Nachweis, Beleg, Evidenzbericht, Selbstauskunft |
| bescheinigt hiermit, wir bestätigen | beobachtet, aufgezeichnet, gemessen |
| konform, erfüllt Art. X, DSGVO-konform | Beleg für Art. X, eine Tatsache, nach der Art. X fragt |
| geprüft durch, auditiert | nachgerechnet, ausgelesen |
| KI-gestützte Bewertung, Passung, Score | Häkchen, Beleg, Herkunft |

Nach **Art. 42/43 DSGVO** darf nur eine Aufsichtsbehörde oder eine nach
EN ISO/IEC 17065 akkreditierte Stelle zertifizieren. BSI C5 attestiert nur ein
zugelassener Prüfer, SecNumCloud qualifiziert nur die ANSSI. Wir sind nichts
davon.

„Zertifiziert" ohne Akkreditierung ist in der EU eine irreführende
Geschäftspraxis — RL 2005/29/EG gegenüber Verbrauchern, RL 2006/114/EG zwischen
Unternehmen. Das ist ein echtes Risiko, kein theoretisches.

Vanta, Drata und Secureframe schreiben aus demselben Grund überall, SOC 2 sei
eine *attestation* und keine *certification*. Ihnen hier zu folgen ist der
sichere Weg, nicht der ängstliche.

**Ein Test erzwingt das.** Er scannt jeden Geltungssatz und jeden Vorbehalt.
Erweitere ihn, statt dich auf die Durchsicht zu verlassen.

## Jedes Zitat nennt, was es offenlässt

```csharp
new Rechtsbezug(
    Regelwerk.Dsgvo,
    "Kap. V (Art. 44–49)",
    Pflicht: "Eine Übermittlung in ein Drittland braucht eine Garantie.",
    Leser:   "Welche Garantie dieses Ziel deckt, und ob dafür eine "
           + "Übermittlungs-Folgenabschätzung vorliegt.");
```

`Leser` ist **nie leer**, und zwar als Test. Ein Zitat, das seinen eigenen
Artikel erledigte, wäre genau die Anmaßung, gegen die das Vokabular existiert.

In der Oberfläche steht `Leser` **in derselben Zeile** wie das Zitat, nie als
Fußnote. Eine Fußnote überlebt weder einen Screenshot noch das Einfügen in eine
fremde Tabelle.

## Was ein Programm zeigen kann — und was nicht

**Kann:** dass eine Aufzeichnung existiert, automatisch entsteht, einen Zeitraum
abdeckt und seither unverändert ist · dass ein Host in einer bestimmten
Jurisdiktion liegt · dass ein Feld existiert oder nicht existiert · dass ein
Widerruf beim nächsten Zugriff wirkt.

**Kann nicht:** ob ein AV-Vertrag angemessen ist · ob eine Risikobewertung etwas
taugt · ob im Prompt personenbezogene Daten stehen · ob eine Nutzung nach
Anhang III hochriskant ist · ob menschliche Aufsicht wirksam ist.

Wenn eine vorgeschlagene Prüfung die zweite Spalte bräuchte, ist sie keine
Prüfung. Sie ist eine Frage an einen Menschen und gehört in `Leser` oder in
einen Vorbehalt.

## Die Fristen, und warum sie dastehen müssen

Die KI-VO läuft gestaffelt an. Stand heute:

| Pflicht | Seit / ab |
|---|---|
| Verbotene Praktiken, KI-Kompetenz | **02.02.2025** |
| GPAI-Pflichten | **02.08.2025** |
| **Art. 50 Transparenz** gegenüber der Person | **02.08.2026 — gilt** |
| Art. 12 Aufzeichnung, Art. 26 Betreiberpflichten | **02.12.2027** |
| Anhang I eingebettete Hochrisikosysteme | 02.08.2028 |

Ein Dokument, das eine Pflicht von 2027 so darstellt, als binde sie heute, lädt
den Leser ein, zu früh Geld auszugeben. **Nenne das Datum.**

## Die Einstufung gehört nicht uns

Anhang III Nr. 4 Buchst. a nennt KI-Systeme für *„die Einstellung oder Auswahl
natürlicher Personen"*. `scout-service` sucht Menschen, `assessment-service`
bewertet eine Arbeitsprobe. Ob das darunter fällt, ist eine Beurteilung der
**Nutzung** und gehört einem Menschen mit juristischer Ausbildung.

ADR-0022 (keine Zahl über einen Menschen) ist erkennbar der Versuch, gar nicht
erst hineinzugeraten. Ob er trägt, entscheidet dieser Mensch.

**Schreibe nie, dass WorkerTransfer nicht hochriskant ist.** Schreibe, welche
Belege vorliegen und wer die Frage beantwortet.

## Was nie in einen Nachweis gerät

- **Kein Wert.** Keine Schlüssel, Token, Verbindungszeichenfolgen, kein roher
  Ausnahmetext. Anbieter schreiben Endpunkte in Ausnahmen.
- **Kein Name einer Person**, und keine Aussage über eine einzelne Person.
  Das KI-Verzeichnis nennt Anbieter und **zählt** — „drei Menschen auf
  `api.anthropic.com`". Nie, wer. ADR-0026 sagt dasselbe für Ereignisse.
- **Keine Zahl über einen Menschen**, auch nicht als Nebenprodukt. ADR-0022 gilt
  auch für das Werkzeug, das ADR-0022 prüft.

## Souveränität ist kein Regelwerk

Keine Verordnung verlangt digitale Souveränität. Sie ist eine Entscheidung des
Betreibers. Wo davon die Rede ist, misst der Nachweis diese Entscheidung — nicht
die Übereinstimmung mit einer Regel. `Regelwerk` bekommt keinen Eintrag dafür.
