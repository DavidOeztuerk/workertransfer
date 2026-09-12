# ADR-0032: Umkreissuche — mit einer mitgelieferten Ortstabelle, gerundeter Position und einer Zahl für das, was sie nicht weiß

**Status:** angenommen (03.09.2026)
**Betrifft:** jobs-service, `ServiceDefaults`, `web/`
**Verwandt:** ADR-0022 (keine stille Vollständigkeit), ADR-0013 (Einwilligung wirkt sofort), ADR-0024 (der Knopf ist die Einwilligung)

## Der Anlass

Die Stellenliste konnte nach einem Ort suchen — als **Zeichenkette**. Wer „Bonn"
tippte, fand keine Anzeige in Köln, dreissig Kilometer entfernt, und wer nicht in
einer Grossstadt wohnt, musste raten, wie das Unternehmen seinen Ort geschrieben
hat. Gesucht wird aber nicht nach einem Wort, sondern nach einer Entfernung.

## Die Entscheidung

Drei Teile, jeder mit einem eigenen Preis.

### 0. Die Anzeige trägt eine Postleitzahl, als eigenes Feld

`Stelle.Postleitzahl`, freiwillig, höchstens zehn Zeichen (`D-10115` schreiben
genug Leute). Der Ortsname bleibt daneben stehen und ist weiterhin das, was auf
der Karte erscheint.

**Zwei Felder statt einem**, weil sie zwei verschiedene Dinge können: der
Ortsname ist für Menschen und darf mehrdeutig sein — „Neustadt" gibt es
zwanzigmal —, die Postleitzahl ist für die Maschine und ist es nicht. Ohne sie
bleibt eine Anzeige auffindbar und fällt nur aus einer Umkreissuche heraus,
wenn ihr Ortsname nicht auflösbar ist; das Formular sagt das, statt ein
Pflichtfeld daraus zu machen. Ein Unternehmen, das nur „bundesweit" anzugeben
hat, soll ausschreiben können.

Bestehende Anzeigen haben kein solches Feld. Sie brauchen keine Nachwanderung:
steht die Postleitzahl im Freitext („10115 Berlin"), wird sie dort gelesen.

### 1. Die Ortstabelle liegt im Code, nicht bei einem Dienst

`ServiceDefaults.Ortskunde` hält **alle** Postleitzahlen und alle nennenswerten
Orte in Deutschland, Österreich und der Schweiz — 16654 Postleitzahlen und
26742 Ortsnamen, aus GeoNames (CC BY 4.0), erzeugt von
`scripts/plz-tabelle.py`, als eingebettete Ressource von 1,2 MB. Dazu
Haversine. Sie wird mit dem Bild ausgeliefert und funktioniert ohne Netz.

**Die Abdeckung war der Einwand gegen diesen Weg, und sie war vermeidbar.** Der
erste Entwurf hatte hundert von Hand getippte Städte; „Kleinmachnow" fiel
heraus, und das ist genau die Sorte Lücke, die man erst bemerkt, wenn jemand
eine Anzeige nicht findet. Der Grund für die Lücke war nicht der Verzicht auf
einen Geokodierdienst, sondern zu wenig mitgelieferte Daten — und die passen in
eine Textdatei.

**Drei Regeln beim Erzeugen, alle an echten Daten gemessen:**

- **Grosskunden-Postleitzahlen fliegen raus.** `10875` hat im Rohdatensatz
  sechsunddreissig Zeilen mit Firmennamen statt Ortsnamen, mit Koordinaten von
  Stuttgart über Berlin bis Bautzen. Eine Postleitzahl, deren Zeilen um
  Hunderte Kilometer streuen, bezeichnet keinen Ort, sondern einen Empfänger.
- **Gleichnamige Orte entscheidet die Einwohnerzahl, aber nur bei klarem
  Abstand.** „Husum" gibt es fünfmal; der Ort in Nordfriesland hat 20841
  Einwohner, der nächste 2336 — wer „Husum" schreibt, meint den ersten.
  „Neustadt" dagegen ist wirklich mehrdeutig und bleibt unbekannt: ein
  Fehlgriff läge vierhundert Kilometer daneben und fiele niemandem auf.
- **Vierstellige Codes gehören Österreich UND der Schweiz.** Aufgelöst wird das
  über den Ortsnamen der Anzeige; ohne ihn bleibt so ein Code unbekannt.
- **Eine Postleitzahl ohne echten Ortsnamen ist keine.** Die deutschen Daten
  führen unter Grosskunden-Codes Firmen und Behörden als „Ortsnamen", und die
  Streuungsprobe fängt das nicht, wenn alle Zeilen in einer Stadt liegen.
  Gemessen: die Gegenrichtung antwortete für Berlin mit **„Adam Opel GmbH"** und
  für München mit **„Amtsgericht München"**. Ein Name zählt nur, wenn er auch im
  Ortsverzeichnis steht — 3561 Codes fallen deshalb heraus. Der Teil vor dem
  Komma zählt mit: Österreich führt „Wien, Innere Stadt", und ohne diese Zeile
  fiel die Postleitzahl 1010 als angeblicher Grosskunde weg.

**Gegen einen Geokodierdienst** (Nominatim, Google, Mapbox) spricht nichts
Technisches — er wäre vollständiger. Er würde aber bei jedem Speichern einer
Anzeige den Ort eines Unternehmens an einen fremden Server geben, und unsere
Datenschutzseite sagt heute *keine Drittanbieter-Skripte, keine Schriften von
fremden Servern*. Eine Ausnahme davon ist eine Entscheidung und keine
Abkürzung; sie steht nicht in dieser Grösse an.

**Der Preis ist Abdeckung**, und er wird bezahlt, nicht versteckt — siehe 3.

**Es gibt keine Koordinatenspalten**, und das ist gegen den ersten Instinkt.
Zwei Spalten `latitude`/`longitude` auf `jobs` wären schneller und hätten einen
Fehler, den die Ableitung zur Abfragezeit nicht hat: **ein Ort, der der Tabelle
heute fehlt und morgen ergänzt wird, wirkt sofort.** Mit Spalten bräuchte jede
bestehende Zeile eine Nachwanderung, und wer sie vergisst, bekommt eine
Umkreissuche, die still an alten Anzeigen vorbeiläuft — genau die Sorte Fehler,
die grün aussieht. Der Filter läuft deshalb im Speicher, wie der
Fähigkeitsfilter und aus demselben Grund: die Regel steht im Code, nicht in SQL.
Würde es teuer, gehört die Spalte *samt* Nachwanderung nachgezogen.

### 1b. Die Mitte ist der ORT, und der Standortknopf füllt ihn

Ein Umkreis braucht einen Mittelpunkt. Er kommt aus dem Ortsfeld:

- **Getippt.** Wer „Leipzig" schreibt und „50 km" wählt, meint „um Leipzig
  herum" — und braucht dafür weder GPS noch die Erlaubnis dazu. Der Dienst löst
  den Namen mit derselben Tabelle auf, mit der er auch die Anzeigen verortet.
- **Per Knopf.** `GET /jobs/place?lat=…&lon=…` nennt den nächstgelegenen
  bekannten Ort, und die Oberfläche trägt ihn ein. Ein Umkreis um einen
  unsichtbaren Punkt ist eine Zumutung: die Liste ändert sich, und niemand kann
  sagen, wovon aus gemessen wurde.

**Mit einer Entfernung daneben ist der Ort die MITTE und kein Textfilter mehr.**
Beides zugleich schlösse genau die Nachbarorte aus, wegen derer jemand einen
Umkreis wählt — eine Anzeige in Berlin liegt 150 km von Leipzig und trägt das
Wort „Leipzig" nirgends. Ohne Entfernung bleibt der Ort ein Textfilter; das ist
die einzige Stelle, an der ein Feld seine Bedeutung wechselt, und sie ist an das
Feld direkt daneben gebunden.

Ausserhalb von DE, AT und CH antwortet `/jobs/place` mit `null`. Den nächsten
deutschen Ort zurückzugeben wäre eine Behauptung über den Aufenthaltsort, die
niemand aufgestellt hat.

### 2. Die Position der suchenden Person wird gerundet, bevor sie den Browser verlässt

Zwei Nachkommastellen, gut ein Kilometer. `web/src/shared/hooks/useGeolocation.ts`
rundet; der Dienst sieht nie etwas Genaueres.

Der Grund ist nicht Vorsicht, sondern **dass feiner an keiner Antwort etwas
ändern könnte**: eine Anzeige trägt einen Ortsnamen, und ihre Koordinaten sind
die des Stadtmittelpunkts. Ein GPS-Wert mit sieben Nachkommastellen stünde also
in der Adresszeile, in jedem Zugriffsprotokoll und in keiner Rechnung. Das ist
Datensparsamkeit im Wortsinn: nicht erheben, was nichts trägt.

Zwei weitere Regeln gehören dazu:

- **Nur auf Knopfdruck.** Kein Abruf beim Laden der Seite. Der Browser fragt die
  Person, sobald `getCurrentPosition` läuft — ein Aufruf im Effekt hiesse, einen
  Systemdialog aufzuklappen, bevor jemand gesagt hat, dass er nach Entfernung
  filtern will. Der Knopf ist die Einwilligung, wie beim Entwurfsknopf (ADR-0024).
- **Nichts wird gespeichert.** Die Position lebt im Zustand der Seite und endet
  mit ihr: kein `localStorage`, kein Profilfeld, keine Zeile. Ein gemerkter
  Wohnort wäre ein personenbezogenes Datum, das niemand angelegt hat — und über
  das die Auskunft dann schwiege.

### 3. Was der Filter nicht beurteilen kann, wird gezählt und gesagt

`Seitenantwort<T>` trägt ein Feld `omitted`: wie viele Einträge ein Filter
weglassen musste, weil er über sie nichts sagen konnte. Es fehlt in der Antwort,
wenn die Frage sich nicht stellte — eine `0` behauptete, es sei gefragt und mit
„keine" beantwortet worden.

Ohne dieses Feld wäre die Ortstabelle aus 1 nicht vertretbar. Eine Umkreissuche,
die Anzeigen mit unbekanntem Ort stumm weglässt, liefert ein Ergebnis, das
vollständig aussieht und es nicht ist — die **Lüge durch Auslassen** aus
ADR-0022 §3, wörtlich: *„Wer nichts auf GitHub hat, ist nicht schlechter,
sondern woanders. Eine Ansicht, die das nicht sagt, lügt durch Auslassen."* Die
Oberfläche schreibt den Satz über die Liste, nicht klein darunter.

**Unbekannter Ort heisst nicht „weit weg".** `Ortskunde.Finde` gibt `null` und
nie einen Nullpunkt zurück; ein Punkt bei 0,0 machte aus jeder unbekannten
Anzeige eine, die fünftausend Kilometer entfernt ist, statt einer, über die
niemand etwas gesagt hat.

## Was ausdrücklich dazugehört

- **Voll remote ist immer dabei**, unabhängig vom Radius. Von wo aus so eine
  Stelle erreichbar ist, ist keine Frage der Entfernung — und sie wegen eines
  Ortsfilters zu verstecken träfe genau die Anzeigen, die für jemanden
  ausserhalb der Ballungsräume die interessantesten sind.
- **Der kleinste Radius ist zehn Kilometer** (`Umkreis.KleinsterRadiusKm`). Die
  Koordinaten sind Stadtmittelpunkte; ein Radius unter der Ausdehnung einer
  Stadt behauptete eine Genauigkeit, die die Daten nicht haben.
- **Unvollständiges filtert nicht, Unsinniges wird geklemmt.** Ein Radius ohne
  Punkt ergibt keinen Filter und keinen Fehler — dieselbe Haltung wie bei der
  Seitenwahl. Ein Radius von 100000 wird auf 500 geklemmt statt verworfen: wer
  das schickt, meint „weit", und daraus gar keinen Filter zu machen wäre die
  überraschendere Antwort. In der Oberfläche kann der Fall gar nicht entstehen,
  weil das Feld ohne Standort abgeschaltet ist.

- **Bei „vollständig remote" verschwindet der Entfernungsfilter ganz**, und der
  gewählte Radius wird dabei gelöscht. Nicht Geschmack: voll remote
  ausgeschriebene Stellen kommen unabhängig vom Radius durch, die Kombination
  liefert also nachweislich dasselbe wie „nur remote" allein. Ein Bedienelement,
  das sichtbar dasteht und nichts tut, ist schlimmer als keines — und ein
  stehengebliebener Radius zählte unten als aktiver Filter und käme beim
  Zurückschalten unbemerkt wieder.

## Was ausdrücklich NICHT dazugehört

- **Keine Sortierung nach Entfernung.** Die Liste bleibt nach
  Veröffentlichungszeitpunkt geordnet. Eine Entfernung ist ein Filter, kein Rang
   — und ein Rang ist der Anfang der Zahl, die ADR-0022 verbietet.
- **Keine Entfernung an der Karte.** „37 km" neben einer Anzeige wäre eine Zahl,
  die aus einem Stadtmittelpunkt und einer gerundeten Position eine Genauigkeit
  behauptet, die keine der beiden hat.
- **Kein Ort aus Prosa.** Die Tabelle kennt sechsundzwanzigtausend Namen, und
  viele davon sind gewöhnliche Wörter: *Hof* ist eine Stadt in Bayern mit 46000
  Einwohnern, ebenso *Essen*, *Halle*, *Lage* und *Brand*. Solange jede
  Wortfolge einer Ortsangabe nachgeschlagen wurde, lag „Auf dem Hof meiner Oma"
  in Oberfranken. Zerlegt wird deshalb nur, was höchstens drei Wörter hat —
  „Raum Stuttgart" ist eine Lesart, fünf Wörter sind ein Ratespiel. Wer
  prosaisch schreibt, wird über die Postleitzahl gefunden.
- **Keine Umkreissuche nach Menschen.** Dieser Filter ordnet *Stellen*. Eine
  Kandidatenliste nach Entfernung zu filtern wäre eine Aussage über Personen und
  bräuchte ihre Einwilligung dafür, dass ihr Ort überhaupt gefragt wird — eine
  eigene Entscheidung mit eigenem ADR, nicht ein Parameter mehr.

## Wie es festgehalten ist

- `OrtskundeTests` — Lesen der Ortsangabe, drei Strecken (eine davon Ost-West,
  weil eine Formel ohne Kosinusfaktor bei Nord-Süd stimmt und dort um Faktor 1,6
  danebenliegt), und dass Unbekanntes `null` ergibt.
- `UmkreissucheTests` — durch `GET /jobs`: der Radius entscheidet (Potsdam liegt
  mit 27 km *zwischen* den beiden geprüften Radien, Hamburg wäre bei beiden
  gleich), voll remote ist immer dabei, unbekannte Orte werden gezählt, ohne
  Umkreis gibt es kein `omitted`.
- `JobsPage.test.tsx` — die Rundung, das abgeschaltete Feld ohne Standort, der
  abgelehnte Zugriff, der Hinweis.
- `jobs-journey.spec.ts` — die einzige Stelle, an der eine echte
  Ortungsschnittstelle beteiligt ist, und die einzige, die sieht, was wirklich in
  der Adresszeile steht.
