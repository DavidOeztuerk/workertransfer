# Phase 6, Sub-step 6.2 — Passung: in die andere Richtung

Date: 2026-08-03
Status: Entwurf (selbst geprüft)
Related: **ADR-0022** (kein Gesamtscore), [GitHub als Beleg](2026-08-03-github-evidence-design.md), [Kandidatensuche](2026-08-02-candidate-search-design.md), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 6

## Die Frage, die vor allem anderen steht

Der ULTRAPLAN nennt „Scout-Match". Das übliche Bild dahinter: ein Unternehmen
bekommt eine Liste von Menschen, sortiert nach Passung, mit einer Prozentzahl.

**Das ist der Gesamtscore aus ADR-0022 durch die Hintertür.** Eine Zahl, die
einen Menschen zusammenfasst, wird nicht dadurch besser, dass sie „Passung"
heißt statt „Reputation" — und eine Rangfolge von Menschen ist eine Rangfolge
von Menschen, gleich woraus sie gerechnet wurde.

Dazu kommt etwas, das der alte Code auch schon hatte: die Gewichte hätte
niemand begründet, und niemand könnte sie prüfen. Wer auf Platz 7 landet statt
auf Platz 2, erfährt nie, warum — und kann nichts dagegen tun.

## Die Umkehrung

**Die Passung wird der Person gezeigt, nicht dem Unternehmen. Und sie ordnet
Stellen, nicht Menschen.**

Ein Unternehmen hat bereits eine Suche (Sub-step Kandidatensuche): Fähigkeiten
mit UND, Ort, Remote. Das ist ein Filter über eine Menge, die es schon geben
darf — und er trifft eine Auswahl, keine Wertung.

Was der Person fehlt, ist die Gegenrichtung: *„welche Stellen passen zu dem,
was ich kann?"* Die ordnet **Stellen**, und Stellen sind keine Menschen. Man
darf sie reihen, vergleichen und aussortieren, ohne jemandem Unrecht zu tun.

## Was gezeigt wird: eine Liste, keine Zahl

Zu einer Stelle:

> **Du hast 2 von 3 genannten Fähigkeiten:** Python ✓ · Kubernetes ✓ · Go ✗

Kein Prozentwert, keine Note, kein „87 % Übereinstimmung". Eine Prozentzahl
sieht aus wie eine Messung und ist eine Division — und sie verschweigt genau
das, was zählt: **welche** Fähigkeit fehlt. Die Liste sagt es, und damit weiß
die Person, was sie tun könnte.

Auch keine Sortierung nach Passung in diesem Schnitt: die Stellenliste bleibt
nach Datum geordnet. Eine Sortierung wäre serverseitig zu rechnen, und dann
läge doch wieder eine Reihung in der Datenbank.

## Wo gerechnet wird: im Browser

Der Abgleich passiert **in der Oberfläche**, aus dem eigenen Profil der
angemeldeten Person und den Fähigkeiten, die in der Stellenanzeige stehen.

Damit gibt es die Passung nirgends als Datensatz — kein Feld, keine Tabelle,
keine Kennzahl, die später jemand auswertet oder einem Unternehmen zeigt. Sie
existiert nur, solange die Seite offen ist.

Das ist kein technischer Kniff, sondern die Umsetzung der Entscheidung: **was
es nicht gibt, kann auch nicht ausgewertet werden.**

## Was dafür fehlt: Fähigkeiten an der Stelle

`Job` trägt heute Titel, Beschreibung, Ort, Remote-Modus und Art. Es gibt kein
Feld für „was gebraucht wird". Also:

```
Job.skills   [≤ 50] ≤ 20    was die Stelle verlangt
```

Dieselben Regeln wie beim Profil: getrimmt, entdoppelt ohne Rücksicht auf
Groß- und Kleinschreibung, erste Schreibweise gewinnt. **Ein Abgleich zwischen
zwei Listen, die verschieden normalisiert werden, findet zufällige Treffer.**

**Nachtrag beim Bauen — 50 statt der zuerst notierten 60 Zeichen.** Das Profil
lässt 50 zu. Dürfte eine Stelle 60 verlangen, gäbe es Anforderungen, die *keine
Person eintragen kann* — Zeilen, die garantiert nie ein Haken werden. Das sieht
man in keiner der beiden Dateien, deshalb steht die Bedingung als eigener Test
in `tests/test_skill_limits_align.py`: kleiner darf die Stelle sein, größer
nicht.

Öffentlich wie die ganze Stelle: wer sucht, darf sagen, was er sucht.

## Wer davon nichts merkt

- **Wer nicht angemeldet ist**, sieht die Stelle wie bisher — Fähigkeiten als
  Liste, ohne Abgleich. Er hat kein Profil, also gibt es nichts zu vergleichen.
- **Wer kein Profil gepflegt hat**, sieht ebenfalls keinen Abgleich, sondern
  einen Hinweis, dass er dafür Fähigkeiten in seinem Profil braucht. Ein
  „0 von 3" wäre eine Aussage über ihn, die nicht stimmt: er hat nichts gesagt,
  nicht nichts gekonnt.
- **Das Unternehmen** sieht bei einer Bewerbung keine Passungszahl. Es sieht
  das Profil, wie bisher, und rechnet selbst — oder eben nicht.

## Abgrenzung

**Keine Empfehlungen, kein „diese Stellen könnten dich interessieren".** Das
wäre eine stehende Auswertung über eine Person und braucht eine eigene
Einwilligung.
**Keine Benachrichtigung bei neuen Treffern.** Eine stehende Suche, die
zuschlägt, ist etwas anderes als ein Abgleich, den jemand gerade ansieht.
**Kein Abgleich mit dem Lebenslauf oder GitHub.** Aus Repositories auf
Fähigkeiten zu schließen ist genau die Rechnung, die ADR-0022 verworfen hat —
und sie wäre hier nur besser versteckt.
**Kein Ranking von Menschen, nirgends.**

## Selbstprüfung

*Ist das nicht viel weniger, als „Matching" verspricht?* Ja. Was es mehr ist:
nachvollziehbar. Jede Zeile lässt sich prüfen — die Person sieht die Liste, aus
der der Abgleich entsteht, und beide Seiten stehen offen da.

*Und wenn ein Unternehmen die Zahl trotzdem will?* Es hat die Suche. Der
Unterschied ist, dass die Suche eine Bedingung ist, die es selbst formuliert,
und keine Note, die die Plattform vergibt. Wer aus Bedingungen eine Rangfolge
macht, tut das dann in seinem eigenen Kopf — und trägt sie auch.

*Warum überhaupt Fähigkeiten an der Stelle, wenn im Text ohnehin steht, was
gebraucht wird?* Weil ein Text nicht abgleichbar ist, ohne ihn zu deuten. Eine
Liste, die ein Mensch geschrieben hat, ist beides: lesbar und vergleichbar —
und sie behauptet nichts, was nicht jemand hingeschrieben hat.
