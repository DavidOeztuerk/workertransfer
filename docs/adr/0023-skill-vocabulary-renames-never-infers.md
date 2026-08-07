# ADR-0023 — Das Skill-Vokabular benennt um und leitet nie ab

Date: 2026-08-05
Status: Angenommen
Related: ADR-0022 (`worker-github` gelöscht — kein Gesamtscore), ADR-0021 (schlank statt vorrätig), ADR-0004 (Sharing-Regel), [Entwurf](../superpowers/specs/2026-08-05-skill-graph-design.md)

## Zusammenhang

Sub-step 6.2 vergleicht die Fähigkeiten einer Person mit denen einer
Ausschreibung — im Browser, buchstabengetreu (ohne Rücksicht auf Groß- und
Kleinschreibung). Das erzeugt einen Fehler, den es vorher nicht gab:

> Die Stelle verlangt **PostgreSQL**. Im Profil steht **Postgres**.
> Ergebnis: ✗ — eine Lücke, die es nicht gibt.

Der ULTRAPLAN sieht für Phase 6 einen „Skill-Graph" vor: Fähigkeiten aus
Commits, PRs und Reviews ableiten, daraus zehn Dimensionen berechnen, dazu eine
„Wechselwahrscheinlichkeit" und einen Match-Score.

**ADR-0022 hat den Gesamtscore verworfen**, und Sub-step 6.2 hat die
Kandidaten-Rangfolge verworfen. Damit steht die Frage, was von einem
Skill-Graphen überhaupt bleibt.

## Entscheidung

Es bleibt **ein Vokabular**: eine gepflegte Tabelle, die sagt, welche Wörter
dasselbe meinen. Sie lebt in `packages/worker-skills` und wird in den
`Skills`-Wertobjekten von `profile-service` und `jobs-service` angewandt.

**Die Grenze, die diese ADR festschreibt:**

| erlaubt | verboten |
|---|---|
| „Postgres" und „PostgreSQL" sind **dasselbe Wort** | „React" heißt, du kannst **auch JavaScript** |
| eine Aussage über **Sprache** | eine Aussage über **einen Menschen** |

Konkret verboten, dauerhaft und ohne Ausnahme:

1. **Fähigkeiten aus Aktivität ableiten.** Der gelöschte `worker-github` maß
   „Können in einer Sprache" als Anteil an geschriebenen Bytes; eine
   eingecheckte Abhängigkeit schlug damit jede sorgfältige Bibliothek. Ein
   „Skill-Analyzer" ist dieselbe Rechnung unter freundlicherem Namen.
2. **Niveau, Gewicht, Rangfolge, Verwandtschaft.** Nichts im Vokabular, woraus
   sich eine Zahl bauen ließe. Zehn Dimensionen sind keine Verbesserung
   gegenüber einer Zahl, sondern eine Verzehnfachung: zehn Gewichte, die
   niemand begründet hat, statt einem.
3. **Implikationen** („wer A kann, kann auch B"). Verlockend und in der Praxis
   oft richtig — und trotzdem verboten: es schreibt jemandem eine Fähigkeit zu,
   die er nicht genannt hat, an einer Stelle, an der er nicht widersprechen
   kann. Wer React kann und JavaScript nennen will, nennt es.
4. **Wechselwahrscheinlichkeit.** Eine Vorhersage darüber, ob ein Mensch seinen
   Arbeitgeber verlassen wird, ausgeliefert an Arbeitgeber. Der gefährlichste
   Punkt im ganzen ULTRAPLAN, und das genaue Gegenteil einer Plattform, der man
   seinen Marktstatus anvertraut. Wird nicht gebaut — auch nicht „nur intern".

**Es lehnt nie ab und erfindet nie.** Was das Vokabular nicht kennt, bleibt
genau so stehen, wie es getippt wurde. Eine Liste erlaubter Fähigkeiten wäre
eine Behauptung darüber, welche Arbeit es gibt, und läge bei jeder neuen
Technologie und bei jedem Beruf außerhalb der IT falsch.

## Warum ein Paket und kein Dienst

Ein eigener Dienst mit Datenbank, Migrationen und Container für eine gepflegte
Liste wäre genau die Hülle ohne Konsumenten, die ADR-0021 teuer bezahlt hat.

Die Sharing-Regel verbietet geteilte **Domänenmodelle**, nicht geteilte
**Nachschlagedaten**. Das Vokabular gehört keinem Dienst — es ist die Sprache,
die beide sprechen, aus derselben Begründung, aus der `worker-contracts`
existiert. Es enthält keine personenbezogenen Daten und kein Geschäftsverhalten:
eine Tabelle und eine Funktion, ohne eine einzige Abhängigkeit.

## Warum im Wertobjekt und nicht im Router

Weil es dann **überall** gilt: beim Speichern, beim Lesen aus der Datenbank, in
jedem Test. Läge es im Router, käme eine vor diesem Schnitt geschriebene Zeile
weiterhin als „Postgres" zurück, und der Abgleich zeigte der Person eine Lücke,
die es nicht gibt. So braucht es **kein Datenmigrations-Skript**: eine alte
Zeile ist beim nächsten Lesen kanonisch und beim nächsten Speichern auch in der
Datenbank.

Reihenfolge ist tragend: **erst umbenennen, dann entdoppeln.** Andersherum
würde aus „Postgres, PostgreSQL" zweimal derselbe Eintrag.

## Folgen

- Der Abgleich findet, was gemeint ist, ohne dass jemand raten muss.
- Der Browser braucht **keine** Kopie der Tabelle: beide Listen kommen bereits
  kanonisch von ihren Diensten. Eine zweite Tabelle im Frontend wäre eine, die
  irgendwann abweicht.
- Die Liste wird per Pull Request gepflegt — nachvollziehbar und
  widersprechbar. Eine Liste, die sich selbst aus den Daten füttert („diese
  Wörter kommen oft zusammen vor"), wäre wieder eine Auswertung über Menschen.
- Ein Test hält die Tabelle widerspruchsfrei: keine Schreibweise zeigt auf zwei
  Namen, kein Name ist zugleich Alias eines anderen. Sonst hinge das Ergebnis
  an der Reihenfolge des Nachschlagens — unsichtbar, und je nach Laufreihenfolge
  anders.
- Ein weiterer Test prüft die **Entscheidung**, nicht die Funktion: er wird rot,
  sobald das Modul etwas mit „level", „weight", „score", „rank" oder „implies"
  im Namen anbietet.

## Verworfene Möglichkeiten

- **`apps/skills-service`.** Ein Dienst für eine Nachschlagetabelle: Datenbank,
  Migrationen, Container, Health-Check — für Daten, die sich selten ändern und
  niemandem gehören.
- **Vokabular nur im Browser.** Dann normalisierte nur die Anzeige, und was in
  den Datenbanken steht, liefe weiter auseinander. Die Suche des Unternehmens
  (`?skill=…`) fände weiterhin nichts.
- **Aus dem Bestand lernen.** Eine Auswertung über alle Profile hinweg, um
  Synonyme zu finden — genau die Art von Rechnung, gegen die ADR-0022 steht.
