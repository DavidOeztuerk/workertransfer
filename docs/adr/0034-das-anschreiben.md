# ADR-0034: Das Anschreiben — der KI-Verbraucher, der speichert und überarbeitet

**Status:** angenommen (05.09.2026)
**Betrifft:** applications-service, jobs-service, `web/`
**Verwandt:** ADR-0024 (die KI-Naht entwirft auf Anfrage und speichert nichts), ADR-0022 (keine Zahl über einen Menschen), ADR-0013 (Einwilligung wirkt sofort), ADR-0025 (der Postausgang bleibt inhaltsfrei), ADR-0035 (die Bewerbungsmappe)

## Warum es dieses ADR gibt

ADR-0024 schließt zwei Dinge ausdrücklich aus:

> Nichts wird gespeichert — nicht die Eingabe, nicht die Antwort; der Entwurf
> lebt im Formular, bis er gespeichert wird, und dann ist es der Text des
> Autors. […] Es gibt bewusst **keine Plan-Act-Reflect-Schleife**: eine
> Reflexionsstufe heißt zwei Aufrufe, um die eigenen Worte der Person
> ungefragt umzuschreiben.

Ein Anschreiben braucht **beides**. Es entsteht nicht in einem Formularfeld,
sondern über Tage: erzeugen, lesen, an drei Stellen widersprechen, überarbeiten
lassen, wieder lesen, freigeben. Wer das ohne Speicher baut, hat kein
Anschreiben, sondern ein Textfeld, das man nicht verlassen darf.

Dieses ADR ersetzt ADR-0024 nicht. Es beschreibt den **dritten** Verbraucher
und sagt, welche seiner Regeln hier warum anders lauten — und welche
unverändert gelten.

---

## Die drei Verbraucher nebeneinander

| | `/profiles/me/draft` | `/jobs/draft` | **Anschreiben** |
|---|---|---|---|
| Wem hilft es? | einer Person, sich zu beschreiben | einem Unternehmen, seine Anzeige zu formulieren | **einer Person, sich zu bewerben** |
| Spricht es *über* jemanden? | nein | nein | **nein** |
| Kontext | eigenes Profil, ohne Namen | die eigene Anzeige, ohne Firmennamen | **eigene Daten + die öffentliche Anzeige** |
| Gespeichert? | nein | nein | **ja** |
| Überarbeitung? | nein | nein | **ja, auf ausdrückliche Handlung** |

Die erste Zeile trägt die anderen: **kein Verbraucher sagt etwas über einen
Menschen.** Ein Anschreiben ist der Text einer Person über sich selbst,
adressiert an ein Unternehmen, das sie ausgesucht hat. Es bewertet niemanden,
es ordnet niemanden ein, und es entsteht nur, weil jemand einen Knopf gedrückt
hat.

## Warum gespeichert wird

Weil das Artefakt ein **Dokument** ist und kein Vorschlag für ein Feld.

Der Profilentwurf lebt im Formular, weil er genau eine Handlung weit von
seinem Ziel entfernt ist: übernehmen oder verwerfen. Ein Anschreiben ist ein
Vorgang mit Zuständen — es wartet auf Prüfung, es trägt Anmerkungen, es hat
eine Fassungsnummer, es wird irgendwann abgeschickt. Nichts davon lässt sich in
einem Formularfeld halten, und ein Neuladen dürfte es nicht vernichten.

**Was gespeichert wird, ist der Text der Person** — genau wie beim
Profilentwurf, sobald sie speichert. Der Unterschied ist nur, wann das
geschieht: dort am Ende, hier von Anfang an, weil es sonst gar keinen Vorgang
gäbe.

**Die Eingabe an das Modell wird trotzdem nicht abgelegt.** Kein Prompt-Archiv,
keine Einbettungen, kein Gedächtnis über Bewerbungen hinweg. Gespeichert ist
das Ergebnis, weil es das Dokument ist; alles davor ist Durchgang.

## Warum überarbeitet werden darf

ADR-0024 verbietet die *Reflexionsschleife*, und der Grund steht dabei: „zwei
Aufrufe, um die eigenen Worte der Person **ungefragt** umzuschreiben".

Das Wort trägt die Unterscheidung. Hier schreibt ein Mensch eine Anmerkung —
optional an eine markierte Stelle — und drückt danach *Überarbeiten*. Jeder
Aufruf hat einen Auftrag, einen Absender und einen Zeitpunkt. Das ist keine
Schleife, sondern ein Gespräch, in dem der Mensch die Züge macht.

**Auflagen, damit es das bleibt:**

1. **Kein Aufruf ohne Handlung.** Es gibt keinen Zeitgeber, keinen
   Hintergrundlauf, keine automatische Zweitfassung. Wer nichts anmerkt,
   bekommt nichts Neues.
2. **Ohne offene Anmerkung keine Überarbeitung.** Ein Knopf, der ohne Auftrag
   umschreibt, wäre die Reflexionsstufe unter anderem Namen.
3. **Die Anmerkungen sind der ganze Auftrag.** Das Modell bekommt sie
   wörtlich, samt Zitat, und die Anweisung, alles Unkommentierte zu lassen.
4. **Jede Fassung zählt hoch.** Wer nicht sieht, dass sich etwas geändert hat,
   prüft nicht wirklich.

## Was der Kontext trägt — und was nie

**Trägt er:** die eigene Person (Name, Kontakt, Profil, die Stationen des
eigenen Lebenslaufs, die selbst geschriebenen Absatz-Bausteine, Tonfall,
frühester Eintritt, Gehaltsvorstellung) und die **öffentliche Stellenanzeige**
(Titel, Unternehmen, Ort, Beschreibung, genannte Fähigkeiten).

**Trägt er nie:** eine dritte Person. Keine anderen Bewerber, keine
Vergleichszahlen, keine Einschätzung. Ein Test hält die Feldliste des
Kontexttyps fest, wie ADR-0024 es für die anderen beiden tut.

**Der Firmenname darf hier stehen** — anders als bei `/jobs/draft`. Dort wäre
er der Anfang einer Aussage über ein Unternehmen; hier ist er die Anschrift.
Man kann keinen Brief schreiben, ohne zu wissen, an wen.

**Nichts wird erfunden.** Die Anweisung an das Modell verbietet
Qualifikationen, die in den Unterlagen nicht stehen. Das ist keine Höflichkeit:
ein erfundener Satz im Anschreiben ist eine Falschangabe, für die die Person
haftet und nicht die Plattform.

## Der Weg, und warum es zwei Knöpfe sind

```
Entsteht → Pruefen → (Ueberarbeiten ⇄ Pruefen) → Freigegeben → Gesendet
                                                              ↘ Fehlgeschlagen
```

- **Freigeben** ist nur ohne offene Anmerkung möglich.
- **Senden** ist nur aus *Freigegeben* möglich.
- **Beides sind getrennte Handlungen.** Ein Knopf „freigeben und senden" spart
  einen Klick und nimmt der Freigabe ihren Sinn — sie ist der Moment, in dem
  jemand sagt „so, und nicht anders", und der braucht einen eigenen.
- **Nichts geht ohne Menschen hinaus.** Es gibt keinen Pfad von *Entsteht* nach
  *Gesendet*, der ohne zwei menschliche Klicks auskommt, und es darf keinen
  geben. Ein Test fährt den ganzen Weg und prüft, dass jeder Sprung eine
  Handlung verlangt.

**Mehrere auf einmal ist erlaubt, mehrere auf einmal *senden* auch** — aber
erst nach Freigabe je Stück. Die Massenhandlung liegt beim Erzeugen, wo sie
nichts anrichtet; am Ausgang steht sie nicht.

## Ohne eingerichteten Anbieter

Wie bei den anderen beiden (ADR-0024): es wird **nichts** nach außen gerufen,
und die Oberfläche **sagt es**. Ein Entwurf entsteht dann nicht, statt leer zu
entstehen — ein leeres Anschreiben im Zustand *Pruefen* wäre eine Einladung,
es versehentlich freizugeben.

## Was dieses ADR NICHT erlaubt

- **Keine Bewertung des Anschreibens.** Kein „Passungswert", keine
  Verbesserungs-Punktzahl, keine Reihung der eigenen Bewerbungen nach Güte. Das
  wäre ADR-0022 auf dem Umweg über den eigenen Text.
- **Kein Gedächtnis über Bewerbungen hinweg.** Jeder Entwurf beginnt bei der
  Anzeige und den eigenen Daten. Ein Modell, das „aus den letzten zwanzig
  Bewerbungen gelernt" hat, träfe Aussagen über die Person, die sie nie
  getroffen hat.
- **Kein Versand nach außen.** Bewerbungen bleiben plattformintern
  (ADR-0035). Was das System verlassen hat, erreicht keine Löschung mehr.
