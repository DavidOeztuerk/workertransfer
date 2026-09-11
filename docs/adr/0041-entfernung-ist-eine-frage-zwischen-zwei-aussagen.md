# ADR-0041: Entfernung ist eine Frage zwischen zwei Aussagen — nicht ein Radius um einen Menschen

**Status:** angenommen (11.09.2026), **gebaut (11.09.2026)**
**Betrifft:** profile-service, jobs-service, scout-service, `ServiceDefaults`, `web/`
**Verwandt:** ADR-0032 (Umkreissuche ohne fremden Geokoder), ADR-0036 (scout-service), ADR-0022 (keine Zahl über einen Menschen), ADR-0033 (genannt / belegt / vorgeschlagen), ADR-0020 (Sichtbarkeit lebt im Ledger), ADR-0039 (das Berufsfeld folgert nichts)

## Der Anlass

PBI-3 nennt „Umkreis" als einen von fünf Filtern des Scouts. Naiv gebaut heisst
das: eine Koordinate an der Person, ein Radius in der Suche, und wer draussen
liegt, fällt aus der Liste. Jeder dieser drei Schritte ist ein eigener Fehler.

**Der erste ist ein Datum, das niemand angelegt hat.** ADR-0032 hat die
Position der *suchenden* Person ausdrücklich geregelt: auf zwei Nachkommastellen
gerundet, nur auf Knopfdruck, **nichts wird gespeichert** — *„ein gemerkter
Wohnort wäre ein personenbezogenes Datum, das niemand angelegt hat, und über das
die Auskunft dann schwiege."* Ein Koordinatenpaar am Profil wäre genau dieses
Datum, nur dauerhaft und über den *Gesuchten* statt über den Suchenden.

**Der zweite ist eine Zahl über einen Menschen.** „37 km" steht neben einer
Person, sieht aus wie eine Messung und ist eine Behauptung: die Koordinaten
eines Ortsnamens sind ein Stadtmittelpunkt, nicht eine Wohnung. Die Zahl wäre
genauer, als irgendetwas an ihr wahr ist — und sie ordnet, sobald zwei davon
untereinanderstehen (ADR-0022).

**Der dritte ist das Wegfiltern.** Wer 60 km entfernt wohnt und gern 90 pendelt,
verschwindet aus einer 50-km-Suche, ohne je gefragt worden zu sein. Die Suche
hätte über ihn entschieden, was er selbst entscheidet.

## Was wir über Entfernung tatsächlich wissen

Zahlen, damit die Stufen unten nicht geraten sind:

| | |
|---|---|
| mittlerer Arbeitsweg | **17,2 km** |
| pendeln über 30 km | **7,2 Mio** Menschen |
| pendeln über 50 km | **4,1 Mio** |
| pendeln über 100 km | **2,4 Mio** |
| häufigstes Hybridmodell | **3+2** (drei Tage vor Ort, zwei remote) |

Der Mittelwert von 17,2 km ist dabei nicht die interessante Zahl, sondern der
Abstand zu den anderen: Millionen Menschen pendeln ein Vielfaches davon, und ein
Filter, der auf dem Mittelwert steht, schneidet sie alle weg. Deshalb reicht die
Skala bis „egal" und nicht bis 50.

Und das 3+2-Modell ist der Grund, warum die Gegenseite nicht „Ort" sagt, sondern
**Anwesenheit**: bei zwei Tagen im Büro ist eine Stunde Weg etwas anderes als bei
fünf.

## Die Entscheidung

**Beide Seiten sagen etwas. Die Plattform vergleicht die Aussagen und macht
daraus ein Häkchen.**

### 1. Die Person sagt, wie weit sie zu pendeln bereit ist

Zwei Felder am Profil, beide **freiwillig**, beide **nullbar**:

- `pendelbereitschaft_km` — eine **Stufe**, kein Freitextwert:
  **10 / 25 / 50 / 100 / egal**.
- `umzugsbereit` — ja / nein / offen.

**Stufen und keine Zahl**, und das ist der Kern. Eine Zahl lädt zum Rechnen ein
(„37 ≤ 50, also 74 % passend"); eine Stufe ist eine Aussage, die jemand getroffen
hat. Die Stufen folgen den Zahlen oben: 10 liegt unter dem Mittelwert, 25 knapp
darüber, 50 und 100 sind die beiden Schwellen, an denen Millionen stehen, und
„egal" ist die Stufe, die es geben muss, damit niemand sich kleiner machen muss,
als er ist.

Es ist die Herkunftsklasse **genannt** aus ADR-0033: die Person hat es getippt,
niemand hat es abgeleitet.

### 2. Die Stelle sagt, welche Anwesenheit sie verlangt

Ein Feld an der Anzeige: `anwesenheit` — **remote / hybrid / vor_ort**.

Nicht „Homeoffice möglich" als Häkchen, weil das den häufigsten Fall (3+2) nicht
ausdrücken kann und ihn deshalb als „remote" verkauft. Drei Worte, und das
mittlere ist das ehrliche.

> **Beim Bauen stellte sich heraus: es gibt dieses Feld schon.** jobs-service
> führt seit jeher `Remotegrad` mit `None` / `Hybrid` / `Full`, und der
> Kommentar daneben sagt dasselbe wie der Absatz darüber: *„Eine Aufzählung,
> kein Wahrheitswert. ‚Remote möglich?' ist die Frage, die alle stellen, und
> ‚ja/nein' beantwortet sie falsch: hybrid ist der häufigste Fall und keine
> Zwischenstufe von wahr."*
>
> **Es entsteht deshalb kein zweites Feld.** Ein `anwesenheit` neben dem
> `Remotegrad` wären zwei Wahrheiten über dieselbe Frage — genau das, wogegen
> dieses Repository sonst überall argumentiert. Gelesen wird der vorhandene
> Wert, mit seinen eigenen Worten (`none`/`hybrid`/`full`), und nicht
> umbenannt: ein zweiter Wortschatz für dieselbe Sache ginge beim ersten neuen
> Wert auseinander.
>
> Die Lehre ist älter als dieses ADR und steht in CLAUDE.md: **erst suchen,
> dann entscheiden.** Ein ADR, das ein Feld verlangt, hat zuerst
> nachzusehen, ob es schon dasteht.

### 3. Daraus wird ein HÄKCHEN — keine Zahl, kein Wegfiltern

Auf der Trefferkarte steht, wie überall in diesem Dienst, ein Ja oder ein Nein
mit dem Wort daneben:

```
Erreichbarkeit ✓   sagt: bis 50 km · Stelle: hybrid · Berlin ↔ Potsdam
Erreichbarkeit ✗   sagt: bis 10 km · Stelle: vor_ort · Berlin ↔ München
Erreichbarkeit —   keine Angabe zur Pendelbereitschaft
```

Drei Zustände, und der dritte ist so wichtig wie die ersten beiden: **wer nichts
gesagt hat, bekommt kein Nein.** „Nichts gesagt" ist nicht „passt nicht" — das
ist dieselbe Regel wie beim leeren Fähigkeitenfeld (ADR-0022 §3: keine
stillschweigende Vollständigkeit).

**Was ausdrücklich NICHT entsteht:**

- keine Kilometerzahl auf der Karte — die Entfernung wird gerechnet und sofort
  auf ein Häkchen reduziert, sie reist nie in den Vertrag;
- kein Sortieren nach Nähe (ADR-0036 Auflage 1);
- **kein Wegfiltern.** Wer ausserhalb liegt, bleibt in der Liste und trägt ein
  `✗`. Das Unternehmen entscheidet, nicht die Suche — und es sieht, *warum*.

### 4. Aufgelöst wird zur Suchzeit, gespeichert wird nichts Neues

`ServiceDefaults/Ortskunde.cs` steht schon und trägt die Ortstabelle samt
Entfernungsrechnung (ADR-0032). Sie löst den **vorhandenen Freitext-Ort** der
Person und den der Stelle zur Suchzeit auf, im Arbeitsspeicher, für die Dauer
einer Antwort.

**Es entsteht keine Koordinatenspalte an einem Menschen.** Das ist die
Kernauflage dieses ADR: was nicht gespeichert wird, kann nicht verloren gehen,
nicht abgezogen werden, nicht in einem Auskunftsersuchen fehlen und nicht in
einer Sicherung überleben. Der Preis ist, dass ein unauflösbarer Ortsname kein
Häkchen ergibt — und das ist der dritte Zustand oben, der ohnehin gebraucht wird.

Eine Postleitzahl am Profil wäre die naheliegende Verbesserung. Sie ist
**nicht** Teil dieser Entscheidung: ADR-0032 hat sie an der *Anzeige* eingeführt,
wo sie über ein Unternehmen spricht. An einer Person spricht sie über eine
Wohngegend, und das ist eine eigene Frage.

## Die Gegenprobe, die diese Entscheidung trägt

Wer sie umkehren will, beantwortet zuerst diese drei:

1. Wo liegt die Koordinate der Person, und wer hat sie angelegt?
2. Was steht auf der Karte, wenn die Entfernung 37 km beträgt — und was
   passiert, wenn zwei solche Karten untereinanderstehen?
3. Wer entscheidet, dass 60 km zu weit sind: die Person, das Unternehmen, oder
   der Standardwert eines Filters?

## Wie es festgehalten ist

Gebaut am 11.09.2026. Was oben als Auflage stand, hängt jetzt an Reihen:

- `ErreichbarkeitTests` — die drei Zustände und **vier Wege zum Strich**, von
  denen keiner ein Kreuz werden darf: keine Stufe genannt, keine Stelle
  genannt, Ort der Person unbekannt, Ort der Stelle unbekannt. Dazu „remote ist
  ein Ja" und „hybrid zählt wie vor Ort".
- `ScoutreiseTests.Wer_weiter_weg_wohnt_bleibt_in_der_Liste` — die Kernauflage
  als Aussage über die **Länge** der Liste: zwei Menschen, ein Haken, ein Kreuz,
  **zwei Zeilen**. Und der Rumpf enthält kein `km`.
- `AuflagenTests.Das_Erreichbarkeits_Haekchen_traegt_keine_Kilometerzahl` —
  die Feldmenge von `Erreichbarkeit` und die von `TrefferV1`. Wer eine Zahl
  ergänzt, schreibt sie dort hin.
- `Die_Stelle_wird_einmal_je_Seite_gefragt` — sie ist für alle Treffer
  dieselbe.
- `Eine_schweigende_Stelle_kostet_nur_das_Haekchen` — und **nicht** die
  Trefferseite. Der Unterschied zum Ledger ist bewusst: dort ist Schweigen ein
  503, weil eine leere Liste eine Aussage über Menschen wäre; hier fehlt nur
  eine Auskunft *über* die Liste.

**Die Entfernungen in den Reihen sind an der Ortstabelle gemessen, nicht
geschätzt.** Der erste Entwurf nahm Berlin–Leipzig für „zwischen 50 und 100" —
es sind 150 km, und der Test fiel zu Recht. Jetzt steht Berlin–Wittenberg (90)
dort, und eine eigene Reihe prüft die drei benutzten Entfernungen, damit kein
Fall unten auf einer Annahme über die Tabelle steht.

**Keine Koordinatenspalte, nachgemessen:** die Wanderung `Pendelbereitschaft`
legt genau zwei nullbare Textspalten an (`commute_km`, `relocation`) und sonst
nichts.

## Was dieses ADR nicht entscheidet

- Die Postleitzahl am Profil.
- Ob die Anwesenheit auch die Stellensuche der *Person* filtert (dort ist
  Wegfiltern erlaubt: sie entscheidet über sich selbst).
- Reisebereitschaft und Pensum. Beide stehen in
  `SCOUT-UND-BERATER-BESTAND.md` als fehlend und gehören zum Mandat des
  Beraters (ADR-0037), nicht hierher.
