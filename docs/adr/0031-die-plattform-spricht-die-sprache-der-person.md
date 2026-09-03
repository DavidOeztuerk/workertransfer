# ADR-0031: Die Plattform spricht die Sprache der Person

- **Status:** angenommen
- **Datum:** 03.09.2026
- **Ersetzt:** die festgehaltene Entscheidung „keine i18n-Schicht" aus `CLAUDE.md`

## Was bisher galt

`CLAUDE.md` hielt fest:

> The UI is German, but **hardcoded** — there is no i18n layer, and tests assert
> the German literals directly.

Das war für eine Plattform in einem Markt richtig und ist es nicht mehr. Wer
diese Entscheidung umstößt, schreibt den Grund auf — sonst steht in einem Jahr
eine halbe Übersetzung neben einer halben Begründung.

## Die Entscheidung

**Jeder Text, den ein Mensch liest, kommt aus einem Katalog.** Drei Sprachen zum
Start: Deutsch, Englisch, Französisch. Deutsch ist die Quellsprache — die Texte
gibt es, sie sind geschrieben und geprüft, und eine Übersetzung aus dem
Vorhandenen ist ehrlicher als eine Neufassung.

**Die Wahl gehört der Person, die Erkennung ist nur der Anfang.** Ohne
ausdrückliche Wahl entscheidet `navigator.language`; wer wählt, überschreibt das
dauerhaft.

## `system` bleibt ein eigener Wert

Genau wie beim Farbmodus (`preferencesSlice`), und aus demselben Grund, der dort
schon aufgeschrieben steht:

> Wer das zusammenlegt, kann die Wahl nie wieder zurücknehmen.

`"system"` heißt „folge dem Gerät". Wer beim Start daraus `"de"` macht, weil das
Gerät gerade Deutsch sagt, kann danach nie mehr unterscheiden, ob jemand Deutsch
**gewählt** hat oder ob wir nur folgen — und die Oberfläche folgt einem Wechsel
der Systemsprache nicht mehr. Der Wert lautet also
`"system" | "de" | "en" | "fr"`.

## Der Punkt, den man übersieht: die Sprache gehört ans Konto

**Eine Mail geht asynchron raus.** Die Abschlussmail einer Löschung kommt Tage
später, aus dem Outbox-Versand — der `Accept-Language`-Kopf der auslösenden
Anfrage existiert dann längst nicht mehr. Dasselbe gilt für jede
Benachrichtigung, die aus einem fremden Vorgang entsteht: eine
Lebenslauf-Anfrage schreibt an einen Menschen, der gerade gar nichts tut.

Also: **die Sprache ist eine Spalte am Konto**, gesetzt bei der Registrierung
aus `Accept-Language`, änderbar in den Einstellungen, gelesen beim Zustellen.

**Die Outbox trägt sie nicht.** ADR-0025 gilt unverändert: die Zeile hält eine
Kennung und eine Art, keinen Inhalt. Die Sprache wird beim Zustellen aus dem
Konto gelesen — sonst stünde in jeder Sicherung eine weitere Aussage über einen
Menschen, und eine, die beim Ändern der Vorliebe sofort falsch wäre.

## Was synchron antwortet, folgt dem Kopf

Problemdokumente und alles, was direkt auf eine Anfrage antwortet, lesen
`Accept-Language`. Das ist richtig, weil der Aufrufer in diesem Moment da ist —
und es deckt auch den Fall ab, den die Kontospalte nicht kennt: eine Anfrage
ohne Anmeldung.

**Girders Vorgabe wird neutral englisch.** `ErrorMessageService` verdrahtet
heute rund zwanzig deutsche Meldungen fest und ist die einzige Datei in ganz
Girder mit deutschem Text; Girders eigene README führt das seit Langem als
Mangel. Eine Bibliothek, die die Sprache ihres ersten Anwenders festschreibt, ist
für jeden zweiten kaputt.

## Was mit den Tests passiert

Heute prüfen E2E- und Frontend-Tests deutsche Literale. Sie fahren künftig mit
**festgenagelter Sprache**, und **eine** Reise schaltet um und prüft, dass es
wirkt.

Der Grund für die Festnagelung ist nicht Bequemlichkeit: ein Test, dessen
Sprache von der Umgebung abhängt, ist auf dem Rechner der einen Person grün und
auf dem der nächsten rot, und niemand sieht warum. Die eine umschaltende Reise
ist die, die die Zusage dieses ADR wirklich prüft — die anderen prüfen ihre
eigene Sache und sollen sich an der Sprache nicht stören.

## Was das NICHT ändert

- **Die Fachsprache im Quelltext bleibt.** `Einwilligung`, `Stelle`,
  `Bewerbung`, `Marktstatus` sind Begriffe dieser Domäne, keine Oberflächentexte.
  Ein Katalogschlüssel heißt `consent.revoke`, und was er liefert, ist Text.
- **Kein Zwischenspeicher für Einwilligungen** (ADR-0013). Eine Übersetzung
  ändert nichts an der Frage, wann etwas gelesen wird.
- **Keine Bewertung von Menschen** (ADR-0022). Sprache ist keine Eigenschaft, aus
  der irgendetwas abgeleitet wird — sie wird gelesen, um zu antworten, und sonst
  nirgends benutzt.
- **Die Outbox bleibt inhaltslos** (ADR-0025).

## Warum nicht

**Warum kein „nur Englisch".** Der Markt ist deutschsprachig, und die Texte auf
der Löschseite und im Einwilligungsbereich sind sorgfältig geschrieben. Sie
gegen eine Übersetzung zu tauschen, weil Englisch der kleinste gemeinsame Nenner
ist, verlöre genau die Genauigkeit, die dort zählt.

**Warum nicht die Sprache aus der Adresse oder der Zeitzone raten.** Beides ist
eine Aussage über einen Menschen, die er nicht gemacht hat. `navigator.language`
ist die Angabe, die er selbst in seinem Gerät eingestellt hat — die darf man
lesen.
