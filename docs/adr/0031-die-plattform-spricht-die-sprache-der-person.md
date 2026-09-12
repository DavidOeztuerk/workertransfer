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

Also: **die Sprache ist eine Spalte am Konto** (`users.language`), gesetzt bei der
Registrierung aus `Accept-Language`, änderbar über `PUT /account/language`,
gelesen beim Zustellen.

Der Kopf wird **genau einmal** gelesen: bei der Registrierung. Danach nie wieder.
Er ist eine Angabe des Geräts, und ein Gerät darf eine Entscheidung nicht
überschreiben — wer auf einem englischen Rechner Deutsch gewählt hat, soll nicht
beim nächsten Anmelden wieder Englisch bekommen.

`PUT /account/language` weist eine Sprache ohne Texte mit **422** ab, statt
stillschweigend Deutsch zu speichern. Eine Wahl, die nicht wirkt und nichts sagt,
sieht für den Menschen aus wie ein Fehler der Oberfläche.

Der Typ heisst `Kontosprache` und nicht `Sprache`: `Sprache` ist ein
Parser-Kombinator-Paket, das transitiv hereinkommt und den gleichnamigen
Namensraum besetzt. Der längere Name sagt ohnehin das Richtige — es ist die
Sprache des **Kontos**, nicht die der Anfrage.

**Die Outbox trägt sie nicht.** ADR-0025 gilt unverändert: die Zeile hält eine
Kennung und eine Art, keinen Inhalt. Die Sprache wird beim Zustellen aus dem
Konto gelesen — sonst stünde in jeder Sicherung eine weitere Aussage über einen
Menschen, und eine, die beim Ändern der Vorliebe sofort falsch wäre.

## Was synchron antwortet: Englisch auf dem Draht, übersetzt in der Oberfläche

**Nachtrag vom 03.09.2026, nach dem Bauen.** Der ursprüngliche Absatz sagte, ein
Problemdokument solle `Accept-Language` lesen. Gebaut ist etwas anderes, und der
Grund gehört hierher, weil das Versprechen sonst neben dem Code stünde und ihm
widerspräche.

**Ein Problemdokument ist auf dem Draht englisch und beschreibt eine FORM.**
`"malformed request body"`, `"invalid: email, password"`, `"not authenticated"` —
das sind Aussagen über die Anfrage, keine Sätze für einen Menschen. Sie stehen so
schon da, weil `detail` nie den Inhalt nennen darf (nur die Gestalt), und dieselbe
Regel macht sie zu etwas, das man nicht übersetzt, sondern DEUTET.

**Die Oberfläche deutet sie.** `shared/api/fehler.ts` bildet Statuscode auf einen
Grund und einen Katalogschlüssel ab, je Aufrufstelle. Der Mensch liest also
ohnehin nie den Serversatz, sondern den Katalogtext seiner Sprache — und zwar in
jeder Sprache, die die Oberfläche kennt, auch in einer, von der das Backend nie
gehört hat.

**Der Preis der anderen Lösung war der Ausschlag.** `Accept-Language` durch zwölf
Dienste zu ziehen hiesse, den Katalog zwölfmal zu halten. Genau das ist die
Abweichung, gegen die diese Codebasis sonst überall verteidigt wird: zwölf
Antworten auf eine Frage sind zwölf Gelegenheiten, sie verschieden zu
beantworten. Ein Statuscode ist eine, und er ist maschinenlesbar.

**Gemessen, nicht angenommen** (03.09.2026, gegen den laufenden Stapel): jede
Antwort auf dem Draht ist englisch — die eigenen (`"not authenticated"`,
`"Password rejected: must be at least 12 characters"`) und auch Girders eigene
(`"Potentially malicious input detected"`, `title: "Bad request"`). Der befürchtete
deutsche `ErrorMessageService` erreicht hier niemanden: unser
`ProblemDetailsMiddleware` schreibt die Gestalt, und Girder 4.2 antwortet an den
Stellen, die durchkommen, selbst englisch.

**Girders `ErrorMessageService` bleibt trotzdem eine Schuld** — nur keine unsere.
Eine Bibliothek, die die Sprache ihres ersten Anwenders festschreibt, ist für
jeden zweiten kaputt. Sie steht auf Girders Zettel, nicht auf diesem.

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
