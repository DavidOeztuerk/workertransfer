# ADR-0050 — Der alte Arbeitgeber darf vorkommen, mit zwei Zustimmungen

**Datum:** 16.10.2026 · **Status:** angenommen
**Ersetzt:** „Die Plattform kontaktiert den aktuellen Arbeitgeber nicht“
(`docs/superpowers/specs/2026-08-02-transfer-deals-design.md`) — für den Weg,
den beide freigeben · **Berührt:** ADR-0013, ADR-0020, ADR-0025, ADR-0027 §3,
ADR-0049, `docs/VISION-FRAGEN.md` C1, C2, C3, C4, D1, H2, J4, J5, J6

## Zusammenhang

Der Transfer-Entwurf vom 02.08.2026 hat entschieden, dass die Plattform nicht
weiß, wo jemand arbeitet: Ein Datensatz „Anna arbeitet bei X“ neben „Anna hört
zu“ ist genau die Auskunft, die jemanden den Arbeitsplatz kostet. Deshalb
bestätigt die Person am Ende selbst „mein Arbeitgeber lässt mich gehen“, und das
Feld „Ablöse“ im Angebot an die Person hatte keinen Empfänger.

Der Katalog hat das am 06.10.2026 erweitert (C1-B, C3-C, D1): Die neue Firma
darf beim aktuellen Arbeitgeber eine frühere Freigabe anfragen und dafür eine
Ablöse anbieten — **wenn der Arbeitgeber das zulässt und die Person auch**.

## Entscheidung

**Zwei Zustimmungen, und jede ist ein eigener Schritt.**

1. **Der Arbeitgeber** schaltet in seinen Firmeneinstellungen „Anfragen anderer
   Firmen zu unserem Personal zulassen“ ein (`admin`, es bindet die Firma).
   Die Einstellung ist nirgends öffentlich und nicht abfragbar; sie trägt kein
   Wort wie „Insolvenz“. Sie entscheidet nur, ob **neue** Anfragen möglich
   sind — laufende kann der Arbeitgeber jederzeit ablehnen.
2. **Die Person** öffnet den Weg auf der Karriereseite ihres Arbeitgebers.
   **Den Arbeitgeber nennen und zustimmen ist ein und derselbe Schritt:**
   Ohne Zustimmung entsteht der Datensatz „arbeitet bei X“ gar nicht. Schließt
   sie den Weg, fällt der Datensatz, und laufende Verhandlungen enden.

**Die Zustimmung der Person steht im Register**, als
`employer.route:tenant:<X>`, und wird bei jedem Lesen und Schreiben einer Firma
synchron und ohne Zwischenspeicher gefragt (ADR-0013). Die Zeile in
transfer-service (`employer_routes`) ist nur der Zeiger auf X — das Register
lässt sich nicht nach Präfix durchsuchen. Widerruft sie über `/consents`, ist
der Weg sofort zu, auch wenn die Zeile noch steht. Eine Wahrheit, ein Zeiger.

**Was wer sieht:**

| wer | sieht | wann |
|---|---|---|
| neue Firma Y | dass es den Weg gibt, und welche Firma X ist | nur in einem laufenden Vorgang mit der Person, der eine Freigabe braucht, und nur solange beide Zustimmungen stehen |
| alter Arbeitgeber X | den Namen der Person, die anfragende Firma, die Bedingungen | nur wenn eine Firma tatsächlich anfragt — nie eine Liste „wer von uns hat den Weg geöffnet“ |
| beide Firmen | den frühesten regulären Wechsel aus der Kündigungsfrist der Person (H2), als **ihre** Angabe | nur in der Verhandlung |
| die Person | alles an ihrer Verhandlung | immer, auch nach einem Widerruf |

X erfährt nichts, solange niemand fragt. Das Öffnen des Wegs ist für den
Arbeitgeber unsichtbar; sonst wäre es die Auskunft „hört zu“ auf Umwegen.

**Die Ablöse ist der Preis einer früheren Freigabe**, nicht der Verkauf eines
Vertrags (C3). Y nennt einen Monat und eine Ablöse (oder keine), X kann
annehmen, ablehnen oder eigene Bedingungen nennen; angenommen wird immer von
der Seite, die die Bedingungen **nicht** gesetzt hat. Einigen sich die Firmen,
**muss die Person zustimmen** — und sie kann in jedem Schritt ablehnen, auch
weil sie gar nicht wechseln will. **Die Plattform schlägt keinen Preis vor**
und bewegt kein Geld (C4); sie hält die Zahl fest, damit alle dieselbe sehen.

**Das Feld „Ablöse“ im Angebot an die Person fällt weg.** Es hatte nie einen
Empfänger; die Zahl wandert dorthin, wo zwei Firmen sie vereinbaren.

**Die Aufbewahrungsausnahme folgt der Zahl.** „Bezahlt“ (ADR-0027 §3,
`Retention.KeepPaid`) hieß „abgeschlossen mit Gebühr im Angebot“; jetzt heißt
es „abgeschlossen mit vereinbarter Ablöse“. Der Schalter bleibt **aus**: Die
Löschung nimmt Vorgang und Verhandlung mit.

**Benachrichtigt wird über `transfer_update`**, keine neue Art: Die Verhandlung
ist ein Schritt im Wechsel. Die Person erfährt jeden Schritt einer Firma, die
Mitglieder der jeweils anderen Firma ebenso — ohne Namen, ohne Betrag
(ADR-0025). Die Schritte der Person werden nicht gemeldet, wie bisher.

**Zeugnis anfordern (C2) ist ein Musterschreiben im Browser**, auf
`/checklists` unter dem Zeugnis, kein Modell und kein Versand: Endzeugnis nach der Kündigung (§ 109 GewO), Zwischenzeugnis erst
nach einem ausdrücklichen Haken „mir ist bewusst, dass mein Arbeitgeber daraus
schließen kann, dass ich mich umsehe“. Ein allgemeines Muster ist keine
Prüfung eines Einzelfalls (§ 2 RDG); ein Modell, das es für ihren Fall
umschreibt, wäre eine. Gespeichert wird nichts.

## Was nicht gebaut wird

- **X kann nichts anstoßen.** Kein „Personal anbieten“, keine Liste der
  eigenen Leute, kein Weg von X zu Y. Der eigene Fall „Arbeitgeber gibt Personal
  aktiv ab“ (C1, Transfergesellschaft) bleibt vor dem Start zu entscheiden.
- **Person ↔ alter Arbeitgeber auf der Plattform** (Aufhebung, D1 dritter
  Punkt) — noch nicht. Bis dahin: Checklisten und das Zeugnis-Muster.
- **Treuhand** (C4-B) — später, über einen Anbieter mit eigener Lizenz.

## Was es kostet

- transfer-service fragt identity-service an zwei weiteren inneren Türen:
  ob eine Firma existiert (beim Öffnen des Wegs) und den bürgerlichen Namen der
  Person (für X, **nach** der Registerprüfung) — mit dem Dienstgeheimnis, nicht
  mit dem Token der Firma. Dazu die Mitglieder einer Firma für die
  Benachrichtigung.
- Drei Tabellen mehr: `employer_settings` (je Firma, nichts Persönliches),
  `employer_routes` (je Person, `SubjectKeyedTable`, fällt mit der Löschung),
  `release_negotiations` (fällt mit der Löschung).
- **Offen für den Juristen:** J4 (dürfen die Firmen mit beiden Zustimmungen
  Restfrist und Bedingungen austauschen, und ist die Zustimmung der Person
  trotz Abhängigkeit freiwillig?) und J5 (Ablöse als Preis eines
  Aufhebungsvertrags, Sperrzeit).
