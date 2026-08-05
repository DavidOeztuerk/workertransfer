# Phase 6, Sub-step 6.3 — Skill-Graph: das Vokabular, nicht das Urteil

Date: 2026-08-05
Status: Entwurf (selbst geprüft), gebaut
Related: **ADR-0022** (kein Gesamtscore), **ADR-0023** (worker-skills), [Passung](2026-08-03-matching-design.md), [GitHub als Beleg](2026-08-03-github-evidence-design.md), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 6

## Was der ULTRAPLAN wollte

> `apps/developer-service` — Skill-Graph aus **nur mit Zustimmung** geholten
> öffentlichen Signalen (Commits, PRs, Reviews, Actions, Security Advisories…).
> Mehrdimensionale Scores statt einer Zahl (Technical/Architecture/OSS/
> Community/Leadership/Docs/Testing/DevOps/AI/Security) — explainable.
> […] → **Wechselwahrscheinlichkeit** → Match-Score → Vorschlag.

Davon bleibt nach ADR-0022 und Sub-step 6.2 fast nichts, und das muss hier
stehen, damit es nicht später aus Versehen wiederkommt:

- **Fähigkeiten aus Commits ableiten** ist genau die Rechnung, die ADR-0022
  verworfen hat. Der alte Code maß „Können in einer Sprache" als Anteil an
  geschriebenen **Bytes**; eine eingecheckte Abhängigkeit schlug damit jede
  sorgfältige Bibliothek. Ein „Skill-Analyzer" ist dieselbe Rechnung mit einem
  freundlicheren Namen.
- **Zehn Zahlen statt einer** ist keine Verbesserung, sondern eine
  Verzehnfachung. „Mehrdimensional" heißt nur, dass es zehn Gewichte gibt, die
  niemand begründet hat, statt einem.
- **Wechselwahrscheinlichkeit** ist der gefährlichste Punkt im ganzen Plan: eine
  Vorhersage darüber, ob ein Mensch seinen Arbeitgeber verlassen wird,
  ausgeliefert an Arbeitgeber. Eine Plattform, die das berechnet, ist das
  Gegenteil einer, der man seinen Marktstatus anvertraut. Sie wird hier nicht
  gebaut, auch nicht später, auch nicht „nur intern".

## Was übrig bleibt — und es ist nicht wenig

**Ein Vokabular.** Die einzige Aufgabe eines Skill-Graphen, die niemandem etwas
unterstellt: dafür sorgen, dass zwei Menschen, die dasselbe meinen, auch
dasselbe Wort benutzen.

Der Bedarf ist nicht theoretisch — Sub-step 6.2 hat ihn erzeugt. Der Abgleich
vergleicht auf Gleichheit (ohne Rücksicht auf Groß-/Kleinschreibung, aber sonst
buchstabengetreu). Also:

> Die Stelle verlangt **PostgreSQL**. Im Profil steht **Postgres**.
> Ergebnis heute: ✗ — eine Lücke, die es nicht gibt.

Die Person sieht eine Anforderung, die sie erfüllt, als Fehlstelle. Im
schlechtesten Fall bewirbt sie sich deshalb nicht.

## Die Grenze, die alles trägt

**Das Vokabular benennt um. Es leitet nichts ab.**

| erlaubt | verboten |
|---|---|
| „Postgres" und „PostgreSQL" sind **dasselbe Wort** | „React" heißt, du kannst **auch JavaScript** |
| eine Aussage über **Sprache** | eine Aussage über **einen Menschen** |

Der zweite Fall ist verlockend und in der Praxis oft richtig. Er ist trotzdem
verboten: er schreibt jemandem eine Fähigkeit zu, die er nicht genannt hat, und
zwar an einer Stelle, an der er nicht widersprechen kann. Wer React kann und
JavaScript nennen will, nennt es.

Daraus folgt auch: **kein Niveau, kein Gewicht, keine Rangfolge, keine
Verwandtschaft** — nichts, woraus sich später eine Zahl bauen ließe. Das
Vokabular ist eine Umbenennungstabelle und darf nie mehr sein (ADR-0023).

## Wo es steht

`packages/worker-skills` — ein Paket, kein Dienst.

Ein eigener Dienst mit Datenbank und Container für eine gepflegte Liste wäre
genau die Hülle ohne Konsumenten, die ADR-0021 teuer bezahlt hat. Und die
Sharing-Regel verbietet geteilte **Domänenmodelle**, nicht geteilte
**Nachschlagedaten**: das Vokabular gehört keinem Dienst, es ist die Sprache,
die beide sprechen — dieselbe Begründung, aus der `worker-contracts` existiert.

## Wann umbenannt wird: beim Bauen des Wertobjekts

Nicht beim Schreiben, nicht beim Lesen — **im `Skills`-Wertobjekt selbst**,
in `profile-service` wie in `jobs-service`.

Damit gilt es überall, wo es überhaupt ein `Skills` gibt: beim Speichern, beim
Lesen aus der Datenbank, in jedem Test. **Kein Datenmigrations-Skript nötig** —
eine alte Zeile mit „Postgres" kommt beim nächsten Lesen als „PostgreSQL"
zurück und wird beim nächsten Speichern so abgelegt.

Reihenfolge ist hier tragend: **erst umbenennen, dann entdoppeln.** Andersherum
würde aus „Postgres, PostgreSQL" zweimal derselbe Eintrag.

## Was die Person sieht

Ihre Schreibweise wird zur bekannten — sichtbar, im Formular, nach dem
Speichern. Sie tippt „postgres", es steht „PostgreSQL" da.

Und die wichtigste Eigenschaft: **das Vokabular lehnt nie etwas ab und erfindet
nie etwas.** Was es nicht kennt, bleibt genau so stehen, wie es getippt wurde.
Eine Liste erlaubter Fähigkeiten wäre eine Behauptung darüber, welche Arbeit es
gibt — und sie läge bei jeder neuen Technologie und bei jedem Beruf außerhalb
der IT falsch.

## Abgrenzung

**Keine Vorschläge beim Tippen in diesem Schnitt.** Eine Auswahlliste, die
führt, ist etwas anderes als eine, die korrigiert — und sie verengt, was Leute
über sich schreiben.
**Kein Lernen aus dem Bestand.** „Diese beiden Wörter kommen oft zusammen vor,
also sind sie dasselbe" wäre eine Auswertung über alle Profile hinweg.
**Keine Verbindung zu GitHub.** Aus Repositories auf Fähigkeiten zu schließen
ist die Rechnung aus ADR-0022.

## Selbstprüfung

*Ist eine Umbenennungstabelle wirklich ein „Skill-Graph"?* Nein — und das ist
die ehrliche Antwort. Es ist der Teil davon, der nach ADR-0022 übrig bleibt.
Der Rest war keine fehlende Arbeit, sondern eine Entscheidung dagegen.

*Wer pflegt die Liste?* Vorerst das Repository, per Pull Request — also
nachvollziehbar und widersprechbar. Eine Liste, die sich selbst aus Daten
füttert, wäre wieder eine Auswertung über Menschen.

*Und wenn zwei Wörter doch nicht dasselbe meinen?* Dann ist der Eintrag falsch
und wird entfernt. Ein Test hält die Tabelle widerspruchsfrei: kein Alias zeigt
auf zwei Namen, und kein Name ist zugleich Alias eines anderen — sonst hinge
das Ergebnis an der Reihenfolge des Nachschlagens.
