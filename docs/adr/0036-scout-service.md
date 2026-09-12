# ADR-0036: scout-service — die Ablösung von `/candidates`

**Status:** angenommen (06.09.2026), **gebaut (11.09.2026)**
**Betrifft:** scout-service, profile-service, consent-service, notification-service, `web/`
**Verwandt:** ADR-0033 (Beleg und Sichtbarkeit), ADR-0020 (Sichtbarkeit im Ledger), ADR-0013 (Einwilligung wirkt sofort), ADR-0030 (Sammelprüfung), ADR-0022 (keine Zahl über einen Menschen), ADR-0024 (die KI-Naht), ADR-0025 (Postausgang inhaltsfrei), ADR-0026 (Ereignisse zählen), ADR-0027 (Löschung)

**Gebaut am 11.09.2026.** Dieses ADR entschied; der Dienst steht jetzt unter
`src/scout-service` und antwortet auf `/scout/*`. Was hier steht, ist deshalb
nicht mehr Vorhaben, sondern Begründung — und die vier Auflagen aus
Entscheidung 2 hängen als `AuflagenTests` an ihr, jede mit einer gemessenen
Gegenprobe.

**Eine Entscheidung kam beim Bauen dazu und steht nicht oben:** die Suche ist
ein **ODER** über die genannten Worte, nicht ein UND wie `GET /candidates`.
Unter UND erfüllt jeder Treffer alle Bedingungen — jedes Häkchen wäre gesetzt,
„welche Fähigkeit fehlt" hätte keine Antwort, und auch die erste Auflage wäre
leer, weil es nichts zu sortieren gäbe. Die Häkchenliste aus Entscheidung 2
setzt das ODER voraus; `/candidates` behält sein UND unverändert.

**Und eine Benennung weicht ab, mit Absicht:** Entscheidung 3 nennt den Hinweis
am Treffer `hinweis`; auf dem Draht heisst er `evidence_state` und trägt ein
WORT (`complete`, `none_released`, `partial`, `unavailable`) statt eines Satzes.
Der Grund ist ADR-0031: die Oberfläche formuliert in der Sprache der lesenden
Person, und ein deutscher Satz auf dem Draht wäre eine Sprache, die der Server
für alle festlegt. Die Zusage selbst ist unverändert — das Feld steht **immer**
da, auch wenn es nichts zu sagen gäbe, damit eine Überarbeitung es nicht
weglassen kann. Und `unavailable` ist ausdrücklich etwas anderes als
`none_released`: „wir wissen es gerade nicht" ist keine Aussage über die Person.

## Der Fund, der dieses ADR ausgelöst hat

[`SCOUT-UND-BERATER-BESTAND.md`](../SCOUT-UND-BERATER-BESTAND.md) hat gemessen:
`GET /candidates` im profile-service ist bereits die halbe Suche. Firmenzwang,
Filter über genannte Fähigkeiten, Ort, Remote, Ledger je Zeile über
`/check-batch`, keine Gesamtzahl, keine Sortierung nach Passung — das alles
steht. Was fehlt, ist die Häkchenliste, die Belege am Treffer, und dass die
Person erfährt, dass sie gefunden wurde.

Der Entwurf in `SCOUT-UND-BERATER.md` wollte `GET /me/scouting` — eine Tabelle
„wer hat wen angesehen". Die Prüfspur im resume-service hat genau diese Tabelle
abgelehnt, weil sie die sensibelste im System wäre. ADR-0033 hat entschieden:
eine **Nachricht**, kein Protokoll. Dieses ADR nimmt das mit.

## Entscheidung 1: Ablösung, nicht Umzug

`GET /scout/candidates` ist der Nachfolger von `GET /candidates`. Mitgenommen,
nicht neu erfunden:

- nur für ein Unternehmen (Person: 403)
- Filter über **genannte** Fähigkeiten, Ort, Remote
- Ledger je Zeile über `/check-batch`; abweichende Länge ist ein Fehler, kein
  Raten
- keine Gesamtzahl (ADR-0026: die Differenz verriete, wie viele Profile *nicht*
  freigegeben sind)
- keine Auffüllung einer kurzen Seite (ADR-0020 §4)
- Reihenfolge `updated_at DESC, id DESC` — stabil, nicht bedeutungstragend
- Seite höchstens 50, Filter höchstens 10
- 503, wenn der Ledger schweigt

`GET /candidates` ist am 11.09.2026 **gefallen**, zusammen mit dem Umzug der
Oberfläche auf `/scout` — zwei Suchen nebeneinander wären zwei Wahrheiten
gewesen. Mit ihm fielen der UND-Zweig im Profilspeicher und die Sammelfrage im
Einwilligungstor von profile-service: beide hatten danach keinen Aufrufer mehr.
`docs/routenkarte.yml` hält den alten Pfad als **tote Tür** fest (404 in allen
vier Spalten), und `LandkarteTests` misst nach, dass wirklich keine Route mehr
dahintersteht.

## Entscheidung 2: vier Auflagen, als Tests

1. **Keine Sortierung nach Passung.** Zwei gleiche Suchen liefern dieselbe
   Reihenfolge. Kein `ORDER BY`, das aus der Person abgeleitet ist.
2. **Keine Zahl über einen Menschen.** Ein Zwilling von `Adr0022Tests` über
   Domain und Contracts. Kein Feld `fit`, `score`, `percent`, `rank`,
   `passung`, `anzahl`. Auch nicht „2 von 3" als einziges, was die Oberfläche
   zeigt — die Häkchenliste nennt, *welche* Fähigkeit fehlt.
3. **Nur Genanntes ist durchsuchbar.** Belege (GitHub-Topics, Stationen) werden
   zum Treffer dazugeholt, nie zum Finden benutzt. Herkunft **genannt** ist die
   Suchspalte (ADR-0033).
4. **Die Ansprache ist ein Entwurf.** Der Dienst schreibt niemandem. Ein
   eigener `IEntwerfer` und ein eigener Kontext — kein gemeinsamer Prompt mit
   einem `if` (ADR-0024). Der Kontext trägt nur schon sichtbare, genannte
   Worte; ein Feld-Set-Test pinnt das.

## Entscheidung 3: der Treffer sagt, was unvollständig ist

Wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders
(ADR-0022 §3). Dieselbe Antwortgestalt, mit einem `hinweis` **in der JSON**,
damit eine Überarbeitung ihn nicht weglassen kann. Keine leere Box, keine
ausgegraute Karte.

Die Häkchenliste und die Belege gehören in den Vertrag, nicht in die
Oberfläche allein. Sonst rechnet der Browser eine Zahl, und ADR-0022 ist durch
die Hintertür da.

## Entscheidung 4: die Anfrage speichern, nie das Ergebnis

Eine gespeicherte Suche hält Filter, keinen Treffer-Cache und keinen
periodischen Lauf. Ein Widerruf muss auf dem nächsten Aufruf wirken
(ADR-0013). Ein Lauf, der nachts sucht und „neue Treffer" an das Unternehmen
mailt, wäre genau der Cache, den ADR-0013 verbietet, plus eine zweite
Benachrichtigung neben der an die Person.

## Entscheidung 5: „dein Profil wurde entdeckt"

Eigene Benachrichtigungsart (Wort `profile_discovered`). Der Satz nennt
**kein Unternehmen**. Der Postausgang bleibt inhaltsfrei: Kennung und Art.
Höchstens **eine je Person und Tag** — eine eigene Kappe je Art, nicht die
stündliche über alle Arten, und keine Tabelle, wer wen angesehen hat.

Es entsteht **kein** `/me/scouting`.

## Entscheidung 6: das Berufsfeld wird NIE ein Filter

**Nicht „später", nicht „offen" — nie.** Zwei Gründe, und der zweite ist der
praktische.

**Es widerspricht ADR-0039 wörtlich.** Dort steht über das Berufsfeld: *„Aus ihm
folgt genau EINE Sache: was die Oberfläche anbietet. Es folgt daraus keine
Berechtigung, keine Sichtbarkeit, keine Sortierung und keine Aussage über einen
Menschen."* Ein Suchfilter macht daraus eine Sichtbarkeitsachse. Wer ihn will,
ändert zuerst ADR-0039 — und muss dann begründen, warum ein Etikett am Konto
darüber entscheidet, wer gefunden wird.

**Und er fände WENIGER, nicht mehr.** Der Scout sucht genannte Fähigkeiten, und
der Wortschatz trägt MIG, WIG, CNC, SPS, Stapler, Gerüst. Wer „MIG" sucht,
findet jeden, der es genannt hat — unabhängig davon, welches Etikett an seinem
Konto hängt oder ob er überhaupt eines gesetzt hat (es ist nullbar und bleibt
es). Ein Berufsfeldfilter davor schnitte genau die weg, die sich nicht
einsortiert haben. Ein Filter, der die Menge kleiner macht, ohne sie genauer zu
machen, ist kein Filter, sondern ein Hindernis.

## Entscheidung 7: die Verfügbarkeit wird NICHT gefiltert

Der Marktstatus liegt in transfer-service und hat seine **eigene Freigabe**: ein
Unternehmen fragt, die Person antwortet (`market_request`). Danach zu filtern
hiesse, „diese Person ist ansprechbar" zu erfahren, **ohne gefragt zu haben** —
über die blosse Länge einer Trefferliste. Genau das schützt der Dreieckskonsens.

Die Zusage bleibt, wie sie ist: wer ansprechbar ist, erfährt man, indem man
fragt. Der Knopf dafür steht auf der Trefferkarte.

## Entscheidung 8: der Umkreis kommt — aber nicht als Radiusfilter

Entfernung ist keine Eigenschaft eines Menschen, sondern das Verhältnis zweier
Aussagen: was jemand zu pendeln bereit ist, und was eine Stelle an Anwesenheit
verlangt. Daraus wird ein **Häkchen**, keine Zahl und kein Wegfiltern — und es
wird nichts Neues gespeichert.

Das ist [ADR-0041](0041-entfernung-ist-eine-frage-zwischen-zwei-aussagen.md).

## Was dieses ADR nicht entscheidet

- Die Stufen des Beraters (ADR-0037).
- Assessment.

## Löschung

Ab der ersten Tabelle ist scout-service Löschempfänger. `"scout"` gehört in
`Loeschempfaenger.Fremde` **und** in `LoeschempfaengerTests.Dienste` — sonst
sieht die Prüfung den zwölften Dienst nicht.
