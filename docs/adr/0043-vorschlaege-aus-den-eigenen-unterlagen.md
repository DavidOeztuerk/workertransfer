# ADR-0043: Vorschläge aus den eigenen Unterlagen — eine Bibliothek im Prozess, ein Index, der mit der Löschung fällt

**Status:** angenommen (12.09.2026)
**Betrifft:** resume-service, `src/shared/WorkerTransfer.Skills`, `web/`
**Verwandt:** ADR-0033 (Beleg und Sichtbarkeit — genannt/belegt/vorgeschlagen), ADR-0039 (Belege sind berufsabhängig), ADR-0004 (kein Scraping, Einwilligung zuerst), ADR-0022 (keine Zahl über einen Menschen), ADR-0023 (der Wortschatz benennt um und folgert nie), ADR-0024 (die KI-Naht: ein Port, nichts gespeichert), ADR-0027 (Löschung: Kaskade und Nachweis), ADR-0035 (die Bewerbungsmappe — Unterlagen und Ablage)

## Warum es dieses ADR gibt

Heute bekommt der Softwareentwickler Vorschläge für sein Profil, und alle
anderen bekommen keine. Der Vorschlagsbereich auf der Profilseite steht seit
ADR-0033 und wird aus drei Quellen gespeist — GitHub-Topics, die Technologien
der Lebenslauf-Stationen, die der eigenen Arbeiten. Zwei davon muss jemand
selbst getippt haben, die dritte setzt ein Repositorium voraus. Der
Elektroniker, der ein Zeugnis hochgeladen hat, geht leer aus.

ADR-0039 hat den Rest dieser Schieflage geräumt: das Berufsfeld ordnet, was die
Oberfläche anbietet, der Wortschatz kennt Werkstatt, Lager und Station, und ein
Meisterbrief wiegt genauso viel wie ein GitHub-Topic. Es hat dabei ausdrücklich
eine Sache offengelassen:

> **Es erkennt nichts aus Dateien.** Texterkennung über die eigenen Unterlagen
> einer Person […] ist PBI-7 und braucht ein eigenes ADR, weil ein Index über
> personenbezogene Texte eigene Auflagen hat.

Das hier ist dieses ADR.

## Was gebaut wird, in einem Satz

Die Person drückt auf der Profilseite einen Knopf; resume-service liest den Text
ihrer hochgeladenen Unterlagen, sucht darin die Namen, die der Wortschatz kennt,
und legt sie als **Fund** neben der Unterlage ab. Die Wörter erscheinen in
demselben Vorschlagsbereich wie die drei anderen Quellen — ein Klick füllt das
Formularfeld, und erst **Speichern** macht daraus eine Nennung.

Damit ist die Brücke aus ADR-0033 — *belegt → vorgeschlagen → genannt* — zum
ersten Mal mit Volltext statt mit GitHub-Topics gebaut. Die drei Herkunftsklassen
ändern sich **nicht**: ein hochgeladenes Zeugnis ist ein Beleg, das daraus
gelesene Wort ein Vorschlag, und suchbar wird nur, was die Person tippt und
speichert.

---

## Entscheidung 1: der Texterkenner ist eine Bibliothek im Prozess, kein Dienst im Netz

**`PdfPig` (MIT), im eigenen Prozess, hinter dem Port `ITexterkennung`.**

Das ist die Entscheidung, um derentwillen dieses ADR zuerst geschrieben werden
musste, denn sie ist von aussen unsichtbar und im Nachhinein teuer zu drehen.

**Der Gegenstand entscheidet sie.** Ein Arbeitszeugnis nennt den Arbeitgeber, die
Dauer, die Aufgaben, oft die Note und manchmal Krankheitszeiten. Ein
Texterkenner im Netz hiesse: dieses Dokument verlässt die Plattform, damit ein
Fremder Wörter darin sucht — und der Gewinn wäre ein Vorschlag in einem
Formularfeld. Das ist kein Handel, den jemand bewusst schliessen würde; es ist
einer, in den man hineinrutscht, weil die fremde Schnittstelle bequemer ist als
die Bibliothek.

**Der Preis ist gemessen.** `PdfPig` zieht **null** Fremdpakete (`dotnet list
package` samt transitiven nennt nur PdfPig selbst). Das ist derselbe Massstab,
an dem `Girder.Http` den Vorzug vor `Girder.Infrastructure` bekam — dort waren
es 44 Pakete für ein Gateway, das routet. Hier zählt er doppelt: was nicht
mitkommt, kann auch nicht mitlesen.

**Und wenn doch einmal etwas hinausginge, MUSS das Ziel in der Konfiguration
stehen.** Die Egress-Grenze aus `SovereignPlatform` leitet ihre erlaubten Hosts
aus der Konfiguration ab und **weist ab, ohne zu protokollieren**. Genau daran
ist am 10.09.2026 der Anschreiben-Agent gestorben: jeder Auftrag antwortete
`EgressDeniedException`, der Arbeiter startete, schrieb nie, und die Oberfläche
zeigte **gar nichts**. Ein Knopf, der einfach nichts tut, ist der schlechteste
aller Fehler. Der Kommentar steht deshalb am Registrierungspunkt in
`ResumeInfrastructure` — dort, wo jemand den Erkenner austauschen würde, und
nicht in diesem Dokument, das er dabei nicht liest.

### Keine Texterkennung auf Bildern — und das wird gesagt, nicht verschwiegen

Ein abfotografierter Gesellenbrief ist ein Bild. Ein Scan ohne Textschicht auch.
Aus beiden liest `PdfPig` nichts, und OCR ist eine **eigene Entscheidung**, keine
Erweiterung: sie bräuchte native Binärdateien und Sprachdaten im Image.

Die Antwort unterscheidet deshalb **drei** Zustände, und sie dürfen nie zu zweien
werden:

| Zustand | woran man ihn erkennt | was die Oberfläche sagt |
|---|---|---|
| noch nie gelesen | `read_at` ist `null` | der Knopf, sonst nichts |
| gelesen, kein Text darin | `has_text: false` | *„Aus ‚Meisterbrief' war kein Text zu lesen: die Datei ist ein Bild."* |
| gelesen, kein bekanntes Wort | `has_text: true`, `terms` leer | *„Gelesen. Darin stand kein Wort, das wir kennen — das heisst nicht, dass nichts drinsteht."* |

Die zweite und dritte Zeile zusammenzulegen wäre ADR-0022 §3, wörtlich: *wer
nichts auf GitHub hat, ist nicht schlechter, sondern woanders — eine Ansicht,
die das nicht sagt, lügt durch Auslassung.* Einem Menschen wortlos keine
Vorschläge zu zeigen, weil sein Meisterbrief ein Foto ist, ist dieselbe Lüge in
klein.

---

## Entscheidung 2: gefunden wird, was der Wortschatz kennt — und nichts sonst

`WorkerTransfer.Skills.Wortfund` durchsucht den Text nach den kanonischen Namen
und ihren Schreibweisen aus `Wortschatz`. Es findet `schweissfachmann` und bietet
`Schweißfachmann` an. Das ist eine Aussage über **Sprache** und damit genau das,
was ADR-0023 erlaubt.

**Was es nicht tut, ist der eigentliche Inhalt dieser Entscheidung.** Die
naheliegende Alternative wäre eine Heuristik: grossgeschriebene Wörter,
Wortgruppen hinter „Kenntnisse:", seltene Substantive. Jede davon ist eine
Vermutung über einen Menschen, gebildet aus der **Gestalt** seines Dokuments —
und sie liegt bei jedem Zeugnis anders falsch. Ein Vorschlag, den die Person nie
geschrieben hat und der trotzdem plausibel aussieht, ist die gefährlichste
Ausgabe dieses ganzen Bereichs: sie klickt ihn an, speichert, und behauptet
etwas über sich, das in ihrem Zeugnis nie stand.

**Der Preis ist ehrlich:** was der Wortschatz nicht kennt, wird nicht gefunden.
„Bohrwerksdreher" ist ein echter Beruf und bleibt ungefunden. Wer ihn vermisst,
erweitert den Wortschatz **per Pull Request** — nachvollziehbar und
widersprechbar, wie beim Berufsfeld. Er wird **nie** aus den Unterlagen von
Menschen gelernt; das wäre wieder eine Auswertung über sie.

**Kurze Kürzel gelten nur in Grossbuchstaben.** „wig" ist im Englischen eine
Perücke, „go" ein alltägliches Verb, „ts" und „py" stehen in jedem zweiten
Dateinamen. Unter vier Zeichen und aus reinen Buchstaben muss die Fundstelle
versal sein; „C#", „C++" und „k8s" tragen ein Zeichen, das sie ohnehin
unverwechselbar macht, und gelten weiter in jeder Schreibung. Ein falscher
Vorschlag ist hier teurer als ein fehlender.

---

## Entscheidung 3: gelesen wird auf Auslösung — und deshalb nie beim Hochladen

ADR-0004 verbietet Scraping. Sein Buchstabe wäre mit einem Nachtlauf über die
Ablage eingehalten, sein Sinn nicht, und ADR-0033 sagt das schon für GitHub:

> Eine Plattform, die einem Menschen dauerhaft hinterhersieht, tut etwas anderes
> als eine, die einmal auf seine Bitte hinsieht.

**„Auf Auslösung" ist dabei nur die halbe Regel.** Ein Lesevorgang, der beim
Hochladen mitliefe, wäre formal auch ausgelöst — durch das Hochladen. Gemeint
ist etwas anderes: ein Mensch drückt einen Knopf, liest, was gefunden wurde, und
entscheidet. Deshalb steht der Aufruf des Erkenners ausschliesslich im
Lese-Befehl, und `POST /resumes/me/documents` ruft ihn nicht.

**Gemessen, nicht behauptet.** `ErkennungsreiseTests` hängt einen Zähler um den
echten Erkenner und prüft nach dem Hochladen drei Dinge: der Zähler steht auf
null, die Antwort sagt `read_at: null`, und in der Tabelle steht keine Zeile. Der
Zähler ist der Punkt — ein Lesevorgang, der läuft und sein Ergebnis wegwirft, hat
das Zeugnis trotzdem gelesen.

**`GET …/terms` liest nichts.** Die Profilseite fragt es beim Laden, und sie darf
das, weil dabei keine Datei geöffnet und keine Zeile geschrieben wird. Wer den
Lesevorgang dort anhängt, macht aus dem Öffnen einer Seite genau den
Hintergrundlauf, den der Knopf daneben vermeidet.

**Der Knopf liest jedes Mal alles neu.** Nur die ungelesenen zu nehmen wäre
sparsamer und falsch: der Wortschatz wächst per Pull Request, und ein Zeugnis,
das vor einer Erweiterung gelesen wurde, trüge seinen Fund sonst für immer
unvollständig.

---

## Entscheidung 4: der Index lebt in resume-service und fällt mit der Löschung

`resume_document_terms` — eine Zeile je Unterlage, mit `subject_id`,
`document_id`, `has_text`, den gefundenen Namen und dem Lesezeitpunkt.

**Er liegt dort, wo die Dateien liegen.** Nicht in profile-service, nicht in
scout-service. Ein Index über personenbezogene Texte ist der eine Ort, an dem
dieses System so etwas zulässt, und die Erlaubnis hängt an genau dieser
Bedingung: er steht im selben Dienst wie die Unterlagen, aus denen er stammt, und
wird von derselben Löschung getroffen (ADR-0027). Läge er anderswo, wäre er eine
zweite Kopie personenbezogener Daten neben der Kaskade — und irgendwann eine, die
sie nicht erreicht.

**Gemessen an der Reihe, nicht am Quelltext.** Nach einer Löschung sind auch die
Unterlagen weg, `GET …/terms` antwortet also ohnehin leer; ein Test, der nur die
Antwort ansieht, wäre grün, während die Tabelle die Wörter aus den Zeugnissen
eines gelöschten Menschen weiter hielte. Gezählt wird deshalb **in der
Datenbank**. Dieselbe Messung gilt für den kleineren Weg: wer eine einzelne
Unterlage wegnimmt, nimmt ihren Fund mit — ein Beleg ohne seinen Gegenstand wäre
der Wortlaut eines Zeugnisses, das die Person eben gelöscht hat.

**Gespeichert werden Namen, nie der Wortlaut.** Der gefundene Volltext lebt im
Arbeitsspeicher des einen Aufrufs. Eine Spalte mit dem Text eines Zeugnisses wäre
eine zweite Kopie der Unterlage — diesmal in der Datenbank und damit in jeder
Sicherung und in jedem Abzug, den irgendwer für eine Fehlersuche zieht. Das ist
Wort für Wort die Begründung, aus der die Bytes in der Ablage liegen (ADR-0035).

**Die Wortliste steht als Feld in der Zeile, nicht als eigene Tabelle.** Die
Normalform wäre eine Tabelle je Wort — und sie liesse sich nach dem Wort
durchsuchen. Genau das darf dieser Index nie können.

---

## Entscheidung 5: er ist nicht durchsuchbar, und das ist eine Mechanik

**Durchsuchbar ist allein, was eine Person selbst genannt hat** (ADR-0033). Der
Scout findet Menschen über die Wörter in ihrem Profil, nie über einen erkannten
Text. Diese Zusage hängt hier an drei Stellen, und jede ist ein Test:

1. **Der Speicher kennt keine Frage nach dem Wort.** `IFundSpeicher` hat drei
   Methoden — „was steht in den Unterlagen dieses Menschen", „lege ab", „nimm
   weg". „Welche Menschen tragen ‚Schweißfachmann' in ihren Unterlagen" gibt es
   nicht. Geprüft als **Mengenvergleich**, an der Schnittstelle und an ihrer
   Umsetzung: eine vierte Methode fällt auf, statt mitzulaufen.
2. **Der Weg ins Profil führt durch zwei Handlungen.** Ein Klick füllt das Feld,
   `PUT /profiles/me` macht daraus eine Nennung. `ProfilePage.test.tsx` hält den
   ganzen Weg fest: nach dem Lesen nicht, nach dem Klick nicht, erst nach dem
   Speichern.
3. **Kein Weg nach draussen.** Der Erkenner ruft niemanden an;
   `AuflagenTests.Nach_draussen_gehen_genau_zwei_Tueren` hält fest, dass genau
   zwei Typen in diesem Dienst eine `IHttpClientFactory` nehmen — der Ledger und
   notification-service.

**Die Gegenprobe zu (2) fiel**, und sie ist die, um die es geht: ein `uebernimm`,
das nebenbei speichert, macht die mittlere Erwartung rot. Das wäre „ein erkanntes
Wort landet ohne Speichern im Suchindex".

---

## Was dieses ADR NICHT entscheidet

- **Es macht Belege nicht durchsuchbar.** Ein hochgeladenes Zeugnis bleibt
  „belegt". Wer das ändern will, ändert ADR-0033, nicht dieses.
- **Es wiegt keinen Beleg gegen einen anderen.** Ein Fund aus einem Zeugnis steht
  in derselben Liste wie ein GitHub-Topic, ohne Kennzeichen und ohne Vorrang. Er
  gilt als Beleg an einem **Artefakt** und steht damit hinter dem, was jemand über
  seine eigene Arbeit geschrieben hat — dieselbe Stufe wie ein Topic, nicht eine
  eigene. Dass ein Meisterbrief auf Papier mehr wiegt als ein Repositorium, ist
  wahr und geht diese Plattform nichts an (ADR-0039).
- **Es baut keine KI-Naht.** Kein Modell, kein Einbettungsindex, keine
  Zusammenfassung. Ein Einbettungsindex über Lebensläufe wäre die Suche „nach
  Ähnlichkeit zu einem Menschen", die ADR-0022 ausschliesst.
- **Es entscheidet nicht über OCR.** Bilder bleiben ungelesen und sagen das. Wer
  OCR will, entscheidet zuerst, welche Binärdateien und Sprachdaten ins Image
  gehören — und ob der Erkenner dann noch im Prozess bleibt.
- **Es ändert die Belegarten nicht.** `Unterlagenart` bleibt bei ihren vier
  Werten (ADR-0039).

## Verworfene Möglichkeiten

- **Ein Texterkennungsdienst im Netz.** Siehe Entscheidung 1: der Gegenstand ist
  ein Zeugnis, und der Gewinn wäre ein Vorschlag im Formular.
- **Beim Hochladen lesen.** Bequemer, und es ist genau die Regel aus ADR-0004, die
  dabei fällt. Als Gegenprobe gebaut und gemessen: der Zähler des Erkenners steht
  dann auf eins, und die Reihe wird rot.
- **Den Volltext speichern.** Er wäre bequem für eine spätere Volltextsuche — und
  genau die soll es nicht geben.
- **Den Index in profile-service legen**, wo die Vorschläge angezeigt werden. Er
  läge dann neben den Daten, aus denen er nicht stammt, und die Löschung müsste
  ihn an zwei Stellen treffen. Eine davon würde sie irgendwann verfehlen.
- **Wörter erraten statt nachschlagen.** Siehe Entscheidung 2.

## Die Zusagen, als Tests

| Zusage | Prüfung |
|---|---|
| Ohne Auslösung wird kein Dokument gelesen | `ErkennungsreiseTests.Ohne_Ausloesung_wird_kein_Dokument_gelesen` — Zähler, Antwort und Tabelle |
| Ein Klick findet die Wörter im Zeugnis | `Ein_Klick_findet_die_Woerter_im_Zeugnis` |
| Aus einem Fund folgt kein zweites Wort | `Aus_dem_Fund_wird_nichts_gefolgert`, `WortfundTests.Aus_einem_Fund_folgt_kein_zweites_Wort` |
| „Kein Text" ist nicht „nichts gefunden" | `Ein_Bild_sagt_dass_kein_Text_zu_lesen_war` |
| Die Löschung nimmt den Index mit | `Die_Loeschung_der_Person_nimmt_den_Index_mit` — in der Datenbank gezählt |
| Mit der Unterlage fällt ihr Fund | `Mit_der_Unterlage_faellt_ihr_Fund` |
| Der Index kennt keine Frage nach dem Wort | `AuflagenTests.Der_Index_kennt_keine_Frage_nach_dem_Wort` (Mengenvergleich) |
| Er hält Namen, nie den Wortlaut | `Die_Indexzeile_haelt_genau_diese_Felder` (geschlossene Feldmenge) |
| Keine Zahl über einen Menschen | `Kein_Feld_rechnet_ueber_einen_Menschen` |
| Der Erkenner ruft niemanden an | `Nach_draussen_gehen_genau_zwei_Tueren` |
| Erst das Speichern macht eine Nennung | `ProfilePage.test.tsx` — „macht aus einem erkannten Wort erst mit dem Speichern eine Nennung" |
| Ohne Unterlagen wird nichts schlechter | `ProfilePage.test.tsx` — „zeigt ohne Unterlagen keinen Knopf"; `Ohne_Unterlagen_ist_die_Antwort_leer` |

**Fünf Gegenproben, alle gefallen, alle kompilierten:** beim Hochladen lesen; die
Löschung ohne den Index; eine `WerHatAsync`-Frage am Speicher; ein `uebernimm`,
das nebenbei speichert; und die Versalregel entfernt (dann ist „she was wearing a
wig" ein Fund).
