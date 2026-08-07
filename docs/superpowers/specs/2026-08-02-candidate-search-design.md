# Querschnitt — Kandidatensuche: aus einer Liste wird ein Markt

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: [Profile-Design](2026-08-01-profile-service-design.md), ADR-0020 (Consent als Enabler), [Marktzugang (5.3)](2026-08-02-market-access-and-ui-design.md)

## Die Lücke

`/candidates` zeigt jedes freigegebene Profil, nach Änderungsdatum sortiert,
zwanzig auf einmal. Wer jemanden mit Python in Berlin sucht, blättert.

Das ist der Unterschied zwischen einer Vorführung und einem Produkt: die Daten
sind da, sie sind freigegeben, und es gibt keinen Weg, in ihnen etwas zu finden.

## Warum das keine neue Einwilligungsfrage aufwirft

Ein Filter verengt eine Menge, die es schon gibt. Durchsucht wird ausschließlich,
was ohnehin jedem Unternehmen sichtbar ist — `profile.visibility:public`. Wer
nicht freigegeben hat, taucht in keinem Ergebnis auf, egal wie eng gefiltert
wird.

Damit ist auch die naheliegende Sorge beantwortet: *„kann jemand über einen sehr
engen Filter prüfen, ob eine bestimmte Person hier ist?"* Er kann — und er hätte
es ohne Filter durch Blättern auch gekonnt. Ein öffentliches Profil ist
öffentlich; das ist die Bedeutung des Schalters.

Der Ledger bleibt, wo er ist: **erst filtern, dann fragen.** Die Datenbank
liefert eine Seite Kandidaten, und für jeden einzelnen wird der Ledger gefragt
(3.2). Die Reihenfolge umzudrehen wäre schneller und falsch — dann stünde die
Sichtbarkeit in der Datenbank des Profildienstes.

## Die drei Filter

| Filter | Verhalten |
|---|---|
| `skill` (mehrfach) | **UND**, Groß-/Kleinschreibung egal |
| `location` | Teilstring, Groß-/Kleinschreibung egal |
| `remote` | nur `true` — siehe unten |

**Fähigkeiten mit UND.** Wer „Python" und „Kubernetes" eingibt, sucht jemanden,
der beides kann. ODER wäre die technisch einfachere und praktisch nutzlose
Auslegung: sie liefert bei zwei Begriffen mehr Ergebnisse als bei einem.

**Groß-/Kleinschreibung egal**, weil `Skills` beim Speichern schon
case-insensitiv entdoppelt: „Python" und „python" sind dort dieselbe Fähigkeit.
Eine Suche, die sie unterscheidet, widerspräche der eigenen Datenhaltung.

Verglichen wird zur Abfragezeit (`lower()` über die JSONB-Elemente), nicht über
eine gespiegelte Kleinschreibspalte. Eine solche Spalte wäre eine **zweite Kopie
derselben Daten, die sich ändern können** — genau die Sorte Kopie, vor der diese
Codebasis sonst warnt. Der Preis ist ein Scan statt eines Indexzugriffs; bei der
heutigen Größe ist das nichts, und wenn es etwas wird, ist ein GIN-Index über
einen Ausdruck die Antwort, keine zweite Spalte.

**`remote` filtert nur in eine Richtung.** `remote_ok = false` heißt „ich habe
nicht ja gesagt", nicht „ich lehne ab" — der Haken ist eine Zusage, keine
Ablehnung. Ein Filter „nur Leute ohne Remote-Haken" würde Menschen ausschließen,
die schlicht nichts angekreuzt haben. Es gibt ihn deshalb nicht.

**Höchstens zehn Fähigkeiten je Abfrage.** Ohne Deckel baut ein Aufrufer mit
einer URL eine beliebig teure Abfrage. Dieselbe Überlegung wie beim
Seitendeckel, der schon existiert.

## Was sich am Blättern nicht ändert

Eine Seite kann weniger Einträge liefern als angefragt — auch mit Filtern. Bis
zur vollen Seite nachzuladen würde über die Anzahl der Runden verraten, wie
viele Profile **nicht** freigegeben sind. Das galt vorher (3.2) und gilt weiter;
mit engen Filtern fällt es nur häufiger auf.

**Der Cursor trägt die Filter nicht.** Der Client schickt sie bei jeder Seite
mit. Ein Cursor, der Suchbedingungen einpackt, ist ein zweiter Ort, an dem die
Abfrage steht — und beim ersten Mal, wenn beide auseinanderlaufen, blättert
jemand still durch die falsche Menge.

## Endpunkt

`GET /profiles?skill=python&skill=kubernetes&location=berlin&remote=true`

Unverändert `403` ohne aktives Unternehmen, `503` wenn der Ledger schweigt.
Kein neuer Endpunkt: es ist dieselbe Menge mit Bedingungen mehr, und ein zweiter
Weg an dieselben Daten hätte einen zweiten Filter, der irgendwann abweicht —
dieselbe Überlegung wie beim `company`-Filter der Stellensuche (4.4).

## Oberfläche

Ein Suchfeld für Fähigkeiten (Komma getrennt), eins für den Ort, ein Schalter
für Remote. Ein leeres Ergebnis sagt, dass niemand **mit diesen Merkmalen**
freigegeben hat — nicht, dass es niemanden gibt.

**Keine Trefferzahl**, wie bisher schon: sie ist bei einer Liste, die nach der
Freigabe gefiltert wird, ohnehin keine ehrliche Zahl.

## Abgrenzung

**Keine Volltextsuche** über Überschrift und Text. Sie klingt naheliegend und
ist ein eigener Gegenstand (Ranking, Sprache, Stemming) — und ein schlechter
Rang ist schlimmer als kein Rang.
**Kein Matching, keine Empfehlungen.** Beides gehört zu Phase 6/7 und braucht
eine eigene Einwilligung.
**Kein Speichern von Suchen, keine Benachrichtigung bei neuen Treffern.** Das
wäre eine stehende Anfrage an Menschen, die davon nichts wissen.

## Selbstprüfung

*Warum kein ODER als Option?* Weil zwei Filtermodi eine Erklärung brauchen, die
in kein Formular passt, und weil die Alternative bereits existiert: zweimal
suchen.

*Ist ein Scan über alle Profile nicht fahrlässig?* Bei dieser Größe nicht, und
die Alternative wäre heute eine Optimierung ohne Messung. Der Kommentar an der
Abfrage sagt, was zu tun ist, wenn es eng wird.
