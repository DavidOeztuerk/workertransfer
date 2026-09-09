# ADR-0033: Beleg und Sichtbarkeit — was ADR-0022 zurückkommen lässt

**Status:** angenommen (05.09.2026)
**Betrifft:** github-service, profile-service, resume-service, portfolio-service, notification-service, `web/`
**Verwandt:** ADR-0022 (keine Zahl über einen Menschen), ADR-0023 (Wortschatz benennt um, folgert nie), ADR-0020 (Sichtbarkeit lebt im Ledger), ADR-0013 (Einwilligung wirkt sofort), ADR-0026 (Ereignisse zählen, keine Menschen)

## Warum es dieses ADR gibt

ADR-0022 hat ein Paket gelöscht, das einen Menschen auf eine Zahl zwischen 0 und
100 gerechnet hat. Er wird seither als Verbot des ganzen Themas gelesen, und das
hat bereits eine falsche Entscheidung gekostet. Er hat aber einen eigenen
Abschnitt **„was in Phase 6 wiederkommen darf"**, und der ist ausdrücklich:

> Belege mit Herkunft — „hat an *diesem* Projekt *diese* Commits gemacht",
> nachprüfbar, mit Link, **ohne Zwischenrechnung**. Einwilligung zuerst.
> Sichtbarkeit über den Ledger, jederzeit widerrufbar.

Dieses ADR löst das ein. Es ersetzt ADR-0022 nicht und schwächt ihn nicht ab —
es beschreibt, was unter ihm gebaut werden **darf**, und nagelt seine drei
Verbote als Tests fest. Ohne diese Grundlage hängen `scout-service` und
`advisor-service` in der Luft: beide leben davon, dass es Belege gibt und dass
klar ist, was mit ihnen geschehen darf.

---

## Die eine Frage, die alles ordnet: wer hat es gesagt?

Jede Fähigkeit im System trägt ihre Herkunft. Es gibt genau drei Klassen, und
**nur eine ist eine Aussage über einen Menschen**.

| Klasse | Woher | Gilt als | Suchbar |
|---|---|---|---|
| **Genannt** | Die Person hat es in ihr **Profil** getippt | Aussage über die Person | **ja, und nur das** |
| **Belegt** | GitHub-Topics und -Sprachen, Technologien an einer Lebenslauf-Station oder einer Arbeit | Aussage über ein **Artefakt** oder eine **Tätigkeit** | nein |
| **Vorgeschlagen** | Aus einem Beleg erkannt, unbestätigt | **nichts** — liegt in der Oberfläche, bis jemand tippt oder verwirft | nein |

**Nichts wird zur Aussage über einen Menschen, bevor die Person es bestätigt
hat.** Das ist dieselbe Regel wie in ADR-0023 („benennt um, folgert nie"), auf
neue Eingänge angewandt.

### Der Übergang ist zwei Handlungen, nicht eine

Ein Klick auf einen Vorschlag **füllt das Formularfeld**. Erst **Speichern**
macht daraus eine Nennung. Zwei Handlungen, damit keine davon aus Versehen
passiert — und damit es einen Moment gibt, in dem die Person das Wort liest,
bevor es über sie gilt.

Gebaut in `ProfilePage` samt `vorschlaegeAus`; die Reihe
`ProfilePage.test.tsx` hält beide Hälften fest, unter anderem, dass ohne
Speichern nichts hinausgeht.

### Selbst Getipptes steht vor Gefundenem

Die Vorschlagsliste ordnet **Wörter**, nie Menschen. Sie stellt nach vorn, was
die Person über **ihre eigene Arbeit** geschrieben hat (Station, Arbeit), und
danach, was an einem **Artefakt** gefunden wurde (GitHub-Topic). Innerhalb einer
Stufe entscheidet, in wie vielen Quellen ein Wort vorkommt.

**Das ist gemessen, nicht ausgedacht.** Ohne diese Stufe ordnete allein die
Häufigkeit — und ein Konto mit hundert Repositories drückte die drei
Technologien aus dem eigenen Lebenslauf vollständig aus den obersten
vierundzwanzig: „Kafka" stand in zwei Stationen, „nodejs" in Dutzenden
Repositories. Eine Zahl daraus wird nirgends gezeigt, und aus der Reihenfolge
folgt keine Rangfolge über irgendjemanden.

---

## Was ein Beleg tragen darf — und was nicht

**Die Menge, nie der Anteil.** GitHub meldet je Repository ein Byte pro
Sprache. Genau daraus rechnete das gelöschte Paket sein „Können" als
`bytes / total_bytes` — *„eine eingecheckte Abhängigkeit schlägt jede
sorgfältige Bibliothek. Wer wenig und gut schreibt, verliert"* (ADR-0022 §2).
Gespeichert werden deshalb nur die **Namen**. Was nicht abgelegt wird, kann
niemand aufsummieren; ein Test serialisiert einen geholten Beleg und prüft, dass
die Zahlen auch nicht als Text mitreisen.

**Topics sind der stärkste Beleg**, weil sie eine *Nennung* sind: „kubernetes"
hat ein Mensch an dieses Repository geschrieben, kein Zähler abgeleitet.

**Sterne werden weitergegeben, nie ausgewertet.** Sie stehen im Vertrag, weil
GitHub sie meldet. Eine Reihenfolge über Menschen nach Sternen wäre die
ADR-0022-Punktzahl durch die Hintertür, auch wenn sie „Aktivität" hiesse.

**Ein Beleg braucht einen Nachweis.** Ein genanntes GitHub-Konto ist keiner:
sonst trüge jemand `torvalds` ein und zeigte dessen Arbeit als seine. Nachgewiesen
wird über einen Gist oder über GitHubs eigene Anmeldung.

**Die zwei Wege stellen verschiedene Fragen, und das war ein Fund.** Der Gist
braucht einen genannten Namen — er sucht in *dessen* Gists — und der bewiesene
muss ihm entsprechen. Die Anmeldung braucht ihn nicht: GitHub meldet allein das
Konto, das zugestimmt hat, es gibt also nichts zu vergleichen und nichts
unterzuschieben. Beides gleich zu behandeln kostete am 05.09.2026 einen echten
Nachweis: auf der Verbindung stand noch ein Name aus den Beispieldaten, die
Anmeldung war korrekt, die Antwort war 422. Seither entsteht über die Anmeldung
eine Verbindung **ohne genanntes Konto** (`Login = null` — ein Zustand, kein
Fehlen), und der gemeldete Name wird eingetragen statt verglichen. Wo ein Name
genannt wurde, wird er weiterhin verglichen.

**Kein Hintergrundabgleich.** Geholt wird nur auf Knopfdruck. ADR-0004 verbietet
Scraping; der Buchstabe wäre mit einem Nachtlauf eingehalten, der Sinn nicht —
eine Plattform, die einem Menschen dauerhaft hinterhersieht, tut etwas anderes
als eine, die einmal auf seine Bitte hinsieht.

---

## Sichtbarkeit: wie alles andere, über den Ledger

Belege sind sichtbar, wenn `github.visibility:public` gilt — nicht früher, nicht
anders. Kein Feld am Beleg, keine zweite Wahrheit (ADR-0020). Ein Widerruf wirkt
sofort (ADR-0013).

Der Schalter dafür sitzt auf **„Meine Freigaben"**, zusammen mit Profil und
Arbeiten, und er verlangt eine **nachgewiesene** Verbindung: eine bloss genannte
freizugeben hiesse, die Repositories eines fremden Kontos zu zeigen.

Technologien an einer **Station** folgen der Freigabe des Lebenslaufs
(unternehmensweise, ADR-0020), die an einer **Arbeit** der des Portfolios. Keine
davon macht etwas durchsuchbar — siehe die Tabelle oben.

---

## Die Auskunft: eine Nachricht, kein Protokoll

Der Entwurf sah `GET /me/scouting` vor — eine Liste, wer wann in welcher Suche
aufgetaucht ist — und begründete damit den ganzen Scout. **Das wird nicht
gebaut.** Stattdessen bekommt die Person eine **Nachricht**, wie bei LinkedIn
oder Xing: *„Dein Profil wurde von einem Unternehmen entdeckt."*

Der Grund steht in `resume-service` und ist älter als der Entwurf. `Pruefhandlung`
hält nur Zustandsänderungen fest, und der Kommentar sagt warum:

> Only changes of state. A *read* is not in here, and that is a decision:
> recording every time a company looked at a résumé would build a record of who
> looked at whom, which nothing in this system reads and which would be the most
> sensitive table in it.

Eine Nachricht löst das ohne diese Tabelle: sie erreicht die Person, sie ist
abbestellbar wie jede andere Art, und sie hinterlässt **kein durchsuchbares
Verzeichnis, wer wen angesehen hat**. Damit ist die Transparenz da, um
derentwillen der Scout gebaut werden darf, und die gefährlichste Tabelle des
Systems entsteht nicht.

**Auflagen, die dazugehören:**

- Die Nachricht ist eine eigene **Benachrichtigungsart** neben
  `ResumeRequest`, `MarketRequest`, `ApplicationUpdate`, `TransferUpdate` — also
  einzeln abbestellbar.
- Sie nennt **kein Unternehmen**. „Ein Unternehmen" genügt; wer sucht, ist eine
  Aussage über das Unternehmen, und die Person kann damit nichts anfangen,
  solange niemand sie angesprochen hat.
- Sie geht über den **Postausgang** (ADR-0025) und trägt darin, wie jede andere,
  nur eine Kennung und eine Art — nie einen Inhalt.
- Sie wird **verdichtet**: höchstens eine je Person und Tag. Eine Nachricht je
  Treffer wäre ein Zähler über die eigene Sichtbarkeit und damit auf Umwegen
  genau das Protokoll, das hier nicht entstehen soll.

---

## Die drei Verbote, als Tests

ADR-0022 verbietet drei Dinge. Sie gelten unverändert, und sie hängen an
Prüfungen, die rot werden, wenn jemand aufräumt:

1. **Keine Zahl über einen Menschen und keine Rangfolge daraus.**
   `Adr0022Tests` durchsucht die Domänen- und Vertragsassemblies nach
   `score`, `rank`, `weight`, `probability`, `fit`, `percent` und hält die
   Feldliste von `Repository` fest — acht Felder, alle abgeschrieben, keins
   gerechnet. Wer eins ergänzt, beantwortet zuerst: kommt es SO von GitHub, oder
   ist es gerechnet?
2. **Keine abgeleiteten Eigenschaften.** Kein Sprachanteil, keine „leadership"
   aus Metadaten. `HttpGitHubTests.Sprachen_kommen_als_Menge_ohne_Bytes`
   serialisiert einen Beleg und prüft, dass die Byte-Zahlen nirgends stehen.
3. **Keine stillschweigende Vollständigkeit.** Wer nichts auf GitHub hat, ist
   nicht schlechter, sondern woanders. Deshalb erscheint für jemanden ohne
   Verbindung **kein Vorschlagsbereich** — auch kein leerer und kein
   ausgegrauter; `ProfilePage.test.tsx` hält das fest. Dieselbe Regel trägt die
   Umkreissuche, die nennt, worüber sie nichts weiss (ADR-0032).

---

## Was dieses ADR NICHT entscheidet

- **`scout-service`** — die Suche selbst, die Ablösung von `/candidates`, die
  Häkchenliste. Eigenes ADR.
- **`advisor-service`** — Mandat und Gespräche. Eigenes ADR. Die Bestandsaufnahme
  warnt dort vor einer eigenen Mandatstabelle: Sichtbarkeit lebt im Ledger,
  Verfügbarkeit im Marktstatus.
- **`assessment-service`** — die Aufgabe. Eigenes ADR, und das mit dem grössten
  Missbrauchspotenzial.

## Was davon schon steht

Vieles, und das ist der Grund, warum dieses ADR beschreibt statt zu entwerfen:

- Die drei Herkunftsklassen und der Zwei-Klick-Übergang.
- Belege mit Topics und Sprachmenge, ohne Bytes.
- Der Nachweis über Gist **oder** GitHub-Anmeldung, mit Namensvergleich.
- Der Sichtbarkeitsschalter auf „Meine Freigaben", nur bei nachgewiesener
  Verbindung.
- Technologien an Station und Arbeit, durch denselben Wortschatz.
- Die Rangfolge „selbst getippt vor gefunden".

**Offen:** die Benachrichtigung „dein Profil wurde entdeckt" — sie entsteht mit
`scout-service`, weil es vorher niemanden gibt, der sucht.
