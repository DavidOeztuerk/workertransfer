# ADR-0039: Belege sind berufsabhängig — GitHub ist eine Quelle, nicht die Quelle

**Status:** angenommen (10.09.2026)
**Betrifft:** identity-service, `src/shared/WorkerTransfer.Skills`, resume-service, portfolio-service, github-service, `web/`
**Verwandt:** ADR-0033 (Beleg und Sichtbarkeit — genannt/belegt/vorgeschlagen), ADR-0022 (keine Zahl über einen Menschen), ADR-0023 (der Wortschatz benennt um und folgert nie), ADR-0020 (Sichtbarkeit lebt im Ledger), ADR-0031 (die Plattform spricht die Sprache der Person), ADR-0035 (die Bewerbungsmappe — Unterlagen und Ablage)

## Warum es dieses ADR gibt

Der Name der Plattform sagt „Arbeiter". Gebaut ist sie für Softwareentwickler,
und **entschieden wurde das nie** — es ist die Summe von Einzelentscheidungen,
die jede für sich richtig aussah. Der Befund ist zählbar:

- Der **Wortschatz** (ADR-0023) führt 26 kanonische Namen. **21 davon sind
  IT** — Sprachen, Datenbanken, Wolkenanbieter, Rahmenwerke. Fünf sind es
  nicht: Buchhaltung, Kundenbetreuung, Projektleitung, Altenpflege,
  Elektroinstallation. Wer „MIG/MAG" tippt, findet niemanden und wird von
  niemandem gefunden, der „MAG-Schweißen" getippt hat.
- Die **einzige nachgewiesene Belegquelle** ist `github-service`. Ein Zeugnis
  ist keine, ein Gesellenbrief ist keine — nicht weil sie schwächer wären,
  sondern weil niemand sie eingebaut hat.
- Der Eintrag **`/github` steht im Kontomenü jedes angemeldeten Kontos**,
  bedingungslos (`SiteHeader.tsx`). Ein Metallbauer bekommt ihn angeboten wie
  ein Backend-Entwickler.

Dabei liegt das Meiste schon da. `Unterlage` in resume-service nimmt Zeugnisse
und Zertifikate an, prüft ihren Typ an den ersten Bytes und legt die Bytes in
die Ablage (ADR-0035); portfolio-service hält Arbeitsproben samt Anhängen. Es
fehlt kein Speicher und kein Endpunkt. Es fehlt, dass es jemandem **angeboten**
wird, der kein Repositorium hat und nie eines haben wird.

Das ist keine Geschmacksfrage. Solange „Beleg" praktisch „GitHub" heißt, sucht
der Scout aus ADR-0036 an drei Vierteln der Arbeitswelt vorbei, und der Berater
aus ADR-0037 berät niemanden, der eine Werkstatt betritt statt eines Büros.

## Die Entscheidung: das Berufsfeld ordnet, was angeboten wird — und sonst nichts

Ein Konto trägt ein **Berufsfeld**: eine Angabe der Person darüber, in welcher
Arbeitswelt sie steht. Es ist **nullbar** und bleibt es. Aus ihm folgt genau
eine Sache, und die ist wichtig genug, um sie zweimal zu sagen: **was die
Oberfläche anbietet.** Es folgt daraus keine Berechtigung, keine Sichtbarkeit,
keine Sortierung und keine Aussage über einen Menschen.

### Die Liste ist geschlossen, und das ist der Grund für die Liste

Elf Werte, in der Datenbank als Etikett, im Code als Aufzählung:

`handwerk` · `industrie_technik` · `bau` · `gesundheit_pflege` ·
`logistik_verkehr` · `gastronomie_hotel` · `handel_verkauf` ·
`buero_verwaltung` · `it_software` · `bildung_soziales` · `sonstiges`

Freitext wäre hier kein Entgegenkommen, sondern ein Fehler. **Aus diesem Feld
folgt eine Navigation**, und eine Navigation muss für jeden möglichen Wert eine
Antwort haben. Bei Freitext hieße die Antwort für alles außer einer Handvoll
Schreibweisen „ich weiß es nicht", und der Unterschied zwischen „ich weiß es
nicht" und „nicht angegeben" wäre für die Oberfläche unsichtbar — sie zeigte
demselben Menschen je nach Tippfehler zwei verschiedene Anwendungen.

Dieselbe Begründung trägt schon `Kontosprache`: drei Sprachen, geschlossen,
*„eine offene Menge hieße eine Mail in einer Sprache, für die es keinen Text
gibt"*. Hier heißt sie: eine Ansicht für ein Feld, für das es keine Ansicht
gibt.

Die Liste ist **erweiterbar, und jede Erweiterung ist eine Entscheidung** —
ein Pull Request, wie beim Wortschatz, nachvollziehbar und widersprechbar. Sie
darf nicht aus den Daten wachsen („diese Wörter tippen viele"), denn das wäre
wieder eine Auswertung über Menschen.

`sonstiges` steht ausdrücklich in der Liste und ist **nicht dasselbe wie
nichts**. Wer `sonstiges` wählt, hat gewählt: seine Arbeit passt in keine der
zehn. Wer nichts wählt, hat nicht gewählt. Das erste ist eine Aussage, das
zweite ein offener Zustand, und sie bekommen dieselbe neutrale Ansicht — aber
nur, weil das heute die richtige Ansicht für beide ist, nicht weil sie
dasselbe wären.

### Kein Pflichtfeld, an keiner Stelle

Die Registrierung bietet ein Auswahlfeld an, dessen Vorauswahl leer ist. Eine
Pflichtangabe an der Anmeldung wäre eine Hürde vor dem ersten Nutzen — und sie
zwänge jemanden, der zwischen zwei Welten steht, sich zu entscheiden, bevor er
gesehen hat, wofür.

**Wer nichts wählt, bekommt die heutige Ansicht.** Das ist die Zusage, an der
diese Arbeit gemessen wird: sie fügt hinzu und nimmt niemandem etwas weg. Ein
bestehendes Konto hat `null` in der neuen Spalte und sieht nach der Wanderung,
was es vorher sah — dieselbe Navigation, dieselben Vorschläge. Kein
Datenwanderungs-Skript, keine Vermutung, kein „wir haben dich als Entwickler
eingeordnet, weil du ein GitHub-Konto verbunden hast". Genau diese Vermutung
wäre eine abgeleitete Eigenschaft ohne Grundlage und damit ADR-0022 §2.

### Belegarten je Feld

Die Tabelle sagt, was einem Feld **angeboten** wird. Sie sagt nicht, was zählt,
und schon gar nicht, wie viel:

| Feld | Belege, die angeboten werden |
|---|---|
| `it_software` | GitHub-Verbindung, Arbeitsproben, Zertifikate |
| `handwerk`, `industrie_technik`, `bau` | Gesellenbrief, Meisterbrief, Schweißerpass, Staplerschein, Arbeitsproben (Fotos) |
| `gesundheit_pflege` | Berufsurkunde, Fortbildungsnachweise, Führungszeugnis (**nur genannt, nie hochgeladen**) |
| `logistik_verkehr` | Führerscheinklassen, ADR-Schein, Fahrerkarte |
| `gastronomie_hotel` | Gesundheitszeugnis (**nur genannt**), Ausbildungszeugnis, Arbeitsproben |
| `handel_verkauf`, `buero_verwaltung`, `bildung_soziales`, `sonstiges` | Zeugnisse, Zertifikate, Referenzen |
| *alle* | Arbeitszeugnisse, Zertifikate, Referenzen |

Die letzte Zeile ist die wichtigste: **jedes Feld erbt die allgemeinen Belege.**
Die feldeigenen kommen dazu, sie ersetzen nichts. Ein Entwickler mit einem
Meisterbrief kann ihn hochladen; die Tabelle entscheidet, was **vorgeschlagen**
wird, nie, was **erlaubt** ist. Eine Liste erlaubter Belege wäre dieselbe
Behauptung darüber, welche Arbeit es gibt, gegen die ADR-0023 den offenen
Wortschatz gesetzt hat.

**Zwei Einträge tragen ein `nurGenannt`, und das ist kein Detail.** Ein
Führungszeugnis und ein Gesundheitszeugnis nennen Dinge über einen Menschen,
die auf keinen Server dieser Plattform gehören — ein Führungszeugnis ist ein
Auszug aus einem Register über Straftaten. Es **darf genannt werden** („liegt
vor, Stand März"), weil Arbeitgeber danach fragen und die Person das sagen
können muss. Es darf **nicht hochgeladen werden**, und deshalb steht es in der
Tabelle mit einem Merker, der den Hochladeknopf gar nicht erst entstehen lässt.
Eine Datei, die nie angeboten wird, muss nicht gelöscht werden.

### Die drei Herkunftsklassen gelten unverändert

ADR-0033 unterscheidet **genannt**, **belegt** und **vorgeschlagen**, und nur
die erste ist eine Aussage über einen Menschen und als einzige durchsuchbar.
Dieses ADR ändert daran **nichts**. Es kommen Quellen dazu, keine Klasse:

- Ein hochgeladenes Zeugnis ist **belegt** — eine Aussage über ein Artefakt,
  genau wie ein GitHub-Repositorium. Nicht durchsuchbar.
- Was daraus als Wort erkannt wird, ist **vorgeschlagen** — es liegt in der
  Oberfläche, bis jemand tippt oder verwirft. Nichts.
- Erst was die Person in ihr Profil schreibt und **speichert**, ist
  **genannt**. Zwei Handlungen, wie bisher.

Ein Meisterbrief wiegt damit genauso viel wie ein GitHub-Topic: er ist ein
Beleg, kein Urteil. Dass er in Papierform mehr Gewicht hat als ein Repositorium,
ist wahr und geht diese Plattform nichts an — sobald sie anfinge, Belege
gegeneinander zu wiegen, wäre sie bei der Zahl aus ADR-0022 angekommen.

### GitHub wird nicht abgewertet

Der naheliegende Fehler bei dieser Arbeit ist, das Pendel durchzuschlagen:
GitHub aus dem Kontomenü zu nehmen, den Dienst zurückzubauen, die
Vorschlagsbrücke zu verkürzen. Nichts davon geschieht.

`github-service` bleibt, wie er ist, samt Nachweis über Gist oder Anmeldung,
samt Topics und Sprachmenge ohne Bytes, samt Sichtbarkeit über den Ledger. Er
verliert genau eine Eigenschaft: **die, die einzige zu sein.** Für ein Konto
mit `it_software` ändert sich nichts — für ein Konto ohne Berufsfeld ebenfalls
nicht.

**Die Route `/github` bleibt erreichbar.** Sie wird für ein Konto mit
`handwerk` nur nicht mehr angeboten. Das ist dieselbe Regel, die im Firmenmenü
schon steht und dort ausdrücklich aufgeschrieben ist: *„es verbirgt, es schützt
nicht"*. Wer die Adresse tippt, bekommt die Seite — Verstecken ist keine
Zugriffskontrolle, und eine Navigation, die man für eine hält, ist gefährlicher
als gar keine.

### Der Wortschatz bekommt das Handwerk, und die Regel bleibt

`WorkerTransfer.Skills` bekommt die Begriffe, die in Werkstatt, Lager, Baustelle
und Station getippt werden: `MIG/MAG`, `WIG`, `CNC`, `SPS`, `Staplerschein`,
`Gerüstbau`, `Schweißfachmann`, `Pflegefachkraft` und weitere.

**Die Grenze aus ADR-0023 verschiebt sich dabei um keinen Millimeter.** Die
Verlockung ist hier größer als in der IT, weil die Fachbegriffe eine sichtbare
Ordnung haben — jeder weiß, dass MIG/MAG ein Schweißverfahren ist. Genau
deshalb steht es hier: **„MIG/MAG" impliziert nicht „Schweißen".** Ein Konto,
das MIG/MAG genannt hat, hat nicht „Schweißen" genannt, und eine Suche nach
„Schweißen" findet es nicht. Wer beides nennen will, nennt beides.

Was der Wortschatz darf, ist ausschließlich dies: `MAG-Schweißen`,
`mig/mag` und `MIG MAG` sind **dasselbe Wort**. Eine Aussage über Sprache. Der
Test dazu ist die Gegenprobe zur Verlockung, nicht die Bestätigung der Funktion.

## Die verworfene Möglichkeit: das Berufsfeld aus dem Verhalten ableiten

Es wäre technisch leicht und wurde deshalb geprüft: wer eine GitHub-Verbindung
hat, ist `it_software`; wer „Pflegefachkraft" ins Profil schreibt, ist
`gesundheit_pflege`. Kein Formularfeld, keine Hürde, sofort für alle
bestehenden Konten gefüllt.

**Verworfen, und zwar aus dem Grund, aus dem ADR-0022 ein Paket gelöscht hat.**
Es wäre eine abgeleitete Eigenschaft über einen Menschen, gebildet aus
Metadaten, ohne dass er gefragt wurde und an einer Stelle, an der er nicht
widerspricht, weil er sie nicht sieht. Sie wäre auch schlicht oft falsch: die
Elektronikerin, die zum Spaß ein Repositorium hat, wäre Entwicklerin; der
Ausbilder in der Pflegeschule wäre `gesundheit_pflege` statt
`bildung_soziales`. Und sie wäre **unwiderlegbar unsichtbar** — eine falsche
Einordnung, die niemand anzeigt, bleibt stehen.

Die zweite verworfene Möglichkeit war ein **eigener Dienst** oder ein achtes
Paket unter `src/shared/` für die Liste. Beides ist zu viel für elf Etiketten:
das Berufsfeld ist eine Spalte auf `users` und gehört damit identity-service,
genau wie `Kontosprache`. Wer es später außerhalb braucht, bekommt es über
`Contracts.Identity` — versionierte Grenz-DTOs, nie ein geteiltes
Domänenmodell.

## Was dieses ADR NICHT entscheidet

- **Es macht Belege nicht durchsuchbar.** Ein hochgeladenes Zeugnis bleibt
  „belegt" und damit unsichtbar für jede Suche. Wer das ändern will, ändert
  ADR-0033, nicht dieses.
- **Es erkennt nichts aus Dateien.** Texterkennung über die eigenen Unterlagen
  einer Person — die Brücke „belegt → vorgeschlagen → genannt" mit Volltext
  statt GitHub-Topics — ist PBI-7 und braucht ein eigenes ADR, weil ein Index
  über personenbezogene Texte eigene Auflagen hat (er lebt im selben Dienst wie
  die Unterlagen und fällt mit der Löschung).
- **Es sortiert nichts nach Feld.** Weder Stellen nach Berufsfeld noch Menschen
  nach irgendetwas. Ein Feld auf einer Stelle, das dem Berufsfeld einer Person
  gegenübergestellt wird, wäre ein zweiter Abgleich neben dem der Fähigkeiten
  — das gehört in den Scout (ADR-0036) und wird dort entschieden.
- **Es ändert an Rechten nichts.** Das Berufsfeld steht nicht im Token, es wird
  von keiner Berechtigungsprüfung gelesen, und keine Route antwortet wegen ihm
  anders. Es wird über `GET /auth/session` mitgeteilt, damit die Oberfläche
  entscheiden kann, was sie anbietet — mehr nicht.
- **Es entscheidet nicht über die Belegarten als Datenmodell.** Die Tabelle
  oben ordnet, was die Oberfläche vorschlägt. `Unterlagenart` in resume-service
  bleibt bei ihren vier Werten (Zeugnis, Zertifikat, Lebenslauf, Sonstiges);
  ein „Meisterbrief" ist ein Zertifikat mit einem Namen, den die Person vergibt.
  Eine Aufzählung mit fünfzig Belegarten wäre eine Behauptung darüber, welche
  Nachweise es gibt, und läge beim ersten ausländischen Abschluss falsch.

## Die Zusagen, als Tests

Ein Absatz, der keine Prüfung hat, ist eine Absichtserklärung. Diese hier haben
eine:

1. **Die Liste ist geschlossen und vollständig.**
   `BerufsfeldTests` hält die elf Etiketten als **Menge** fest — ein
   Mengenvergleich, kein Enthaltensein, damit ein zwölftes Feld auffällt und
   nicht mitläuft. Ein unbekanntes Etikett auf `PUT /account/occupational-field`
   antwortet **422** und speichert nichts: eine Wahl, die nicht wirkt, muss das
   sagen (dieselbe Regel wie bei `PUT /account/language`).
2. **Nichts wird schlechter, wenn niemand wählt.** Ein Konto ohne Berufsfeld
   bekommt `null` über `GET /auth/session`, und die Navigation zeigt denselben
   Eintrag wie heute. `SiteHeader.test.tsx` fährt drei Fälle: kein Feld →
   GitHub sichtbar, `it_software` → GitHub sichtbar, `handwerk` → GitHub weg.
   Der mittlere Fall ist der, der beweist, dass hier nicht abgewertet wird.
3. **Der Wortschatz folgert weiterhin nicht.**
   `Aus_MIG_MAG_folgt_kein_Schweissen` steht neben
   `Aus_React_folgt_kein_JavaScript` und ist dieselbe Prüfung an dem Ort, an
   dem sie schwerer zu halten ist.
4. **Die Belegarten erben.** `belegarten.test.ts` prüft, dass jedes der elf
   Felder die allgemeinen Belege enthält — ein Feld, das nur seine eigenen
   trüge, böte einem Metallbauer kein Arbeitszeugnis an.
5. **Was nur genannt werden darf, wird nie hochgeladen.** Derselbe Test hält
   fest, dass `Führungszeugnis` und `Gesundheitszeugnis` `nurGenannt` tragen
   und dass die Hochladeliste sie nicht enthält.
