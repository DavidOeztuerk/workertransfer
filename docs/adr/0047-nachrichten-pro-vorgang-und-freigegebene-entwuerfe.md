# ADR-0047 — Nachrichten pro Vorgang, und ein Entwurf geht erst nach Freigabe hinaus

**Datum:** 07.10.2026 · **Status:** angenommen
**Ersetzt:** nichts · **Berührt:** ADR-0024 (die KI-Naht), ADR-0025 (Outbox),
ADR-0027 (Löschung), ADR-0034 (Anschreiben), `docs/VISION-FRAGEN.md` D2, E1, E2

## Zusammenhang

Auf der Plattform redeten Person und Firma **gar nicht** miteinander. Nach einer
Bewerbung erfuhr die Person den Stand über fünf Werte und sonst nichts; jede
Rückfrage lief über eine E-Mail-Adresse, die erst bekannt sein musste.

Der Katalog hat entschieden (E1-B): **Nachrichten auf der Plattform, ein
Gespräch pro Vorgang** — und dort schreiben später auch die Agenten, und dort
wird freigegeben (D2). Das ist die Grundlage für den KI-Recruiter (Phase 2) und
den KI-Berater (Phase 3); ohne sie hat keiner der beiden einen Ort.

## Entscheidung

**Ein eigener Dienst, `messages-service` (Port 8015).** Ein Gespräch hängt an
einem *Vorgang* (`kind` + `reference`), nicht an einer Tabelle eines anderen
Dienstes. Phase 1 kennt genau eine Art: **`application`** — die Bewerbung, weil
dort Person und Firma heute schon in einer Beziehung stehen, die beide gewollt
haben. Weitere Arten (Ansprache, Gespräch beim Berater, Transfer) kommen mit
ihrer Phase und mit ihrer eigenen Regel, wer schreiben darf.

**Wer zum Gespräch gehört, entscheidet der Dienst des Vorgangs, nicht dieser.**
Vor jedem Öffnen und jedem Schreiben fragt `messages-service` mit dem Token des
Aufrufers `GET /applications/{id}`. Antwortet applications-service 404, antwortet
messages-service 404 — byte-gleich, ob es das Gespräch gibt oder nicht. Eine
zweite Regel „wer darf diese Bewerbung sehen" hier wäre die zweite Wahrheit.
Gefragt wird **synchron und ohne Zwischenspeicher**, aus demselben Grund wie beim
Ledger (ADR-0013): wer aus der Firma entfernt wurde, liest ab der nächsten
Anfrage nicht mehr mit.

**Zwei Seiten, und jede Nachricht gehört einer.** `person` oder `company`. Auf
der Firmenseite schreibt ein Mitglied *im Namen der Firma* — wer genau, steht
in der Zeile (`author`), aber die Person sieht die Firma, nicht den Kollegen.
`member` genügt (die tägliche Arbeit, siehe CLAUDE.md „In doubt, `member`").

**Ein Entwurf ist eine Nachricht im Zustand `draft`, und nur seine Seite sieht
ihn** (D2). Er entsteht auf Knopfdruck („Antwort entwerfen"), ein Mensch liest,
ändert, und erst **„Freigeben"** macht ihn zu `sent`. „Verwerfen" löscht die
Zeile; ein verworfener Entwurf hinterlässt nichts. Die andere Seite erfährt von
einem Entwurf nie — weder Text noch Dasein.

**Gespeichert wird der Entwurf, nicht die Anfrage an das Modell.** Wie beim
Anschreiben (ADR-0034): der Entwurf ist ein Dokument, das ein Mensch prüft, also
liegt er bis zur Freigabe in der Datenbank. Der Prompt, die Platzhalter-Tabelle
und die Rohantwort liegen nirgends.

**Die Herkunft wird festgehalten, aber nur der eigenen Seite gezeigt.**
`origin` ist `human` oder `agent`; freigegeben hat immer ein Mensch. Ob die
Empfängerseite das sehen muss (Art. 50 KI-VO), ist Frage J2 der Juristenliste.
Bis zur Antwort gilt dieselbe Linie wie beim Anschreiben: der Text gehört dem,
der ihn freigegeben hat.

**Was an das Modell geht:** die gesendeten Nachrichten dieses einen Gesprächs
(die letzten zwanzig), der Titel der Stelle und welche Seite schreibt. Name der
Person und Name der Firma nur als **Platzhalter** (E2). Nie Entwürfe, nie ein
anderes Gespräch, nie etwas aus Profil oder Lebenslauf. Die Regeln verbieten,
die andere Seite einzuschätzen — eine Einschätzung einer Person *für eine Firma*
wäre Anhang III Nr. 4 ohne Ausnahme (D2, D3).

**Welches Modell:** auf der Personenseite der eigene KI-Zugang aus den
Kontoeinstellungen, sonst der Plattform-Standard (Mistral, EU); auf der
Firmenseite der Plattform-Standard.

**Benachrichtigung:** eine neunte Art, `message_received`, über die
inhaltslose Outbox (ADR-0025). Sie nennt weder Absender noch Text.

**Kein Gelesen-Zähler, keine Lesebestätigung.** „Hat die Firma meine Nachricht
gelesen?" ist ein Druckmittel in beide Richtungen und eine Aussage über das
Verhalten eines Menschen. Wer antwortet, hat gelesen.

**Löschung:** `messages` ist Empfänger der Löschkaskade (ADR-0027). Das ganze
Gespräch fällt — auch die Nachrichten der Firma, denn sie handeln von der
Person. Keine Aufbewahrungsausnahme.

## Was es kostet

- Ein Dienst mehr: eine Datenbank, ein Eintrag im Chart, eine Route, eine Zeile
  in der Löschkaskade, eine Spalte mehr in jeder Zählung.
- Ein synchroner Aufruf an applications-service je Öffnen und Schreiben.
- Freitext lässt sich nicht zuverlässig schwärzen. Was ein Mensch in eine
  Nachricht über Dritte schreibt, erreicht das Modell, wenn jemand einen Entwurf
  anfordert. Dort schützt die Wahl des Anbieters, und der Hinweis im Gespräch
  (keine Gesundheitsdaten, Art. 9 DSGVO) steht über jedem Eingabefeld.

## Verworfen

- **Nachrichten in applications-service.** Kürzer für Phase 1, aber jede weitere
  Vorgangsart hätte ihren eigenen Nachrichtenstapel bekommen — und der Agent,
  der in allen schreibt, drei Nähte.
- **Ein Lesestatus.** Siehe oben.
- **Entwürfe nur im Browser halten.** Dann gäbe es keinen Ort, an dem ein
  Agent später etwas zur Freigabe ablegt, ohne dass die Seite gerade offen ist.

## Ergänzung 07.10.2026 — die Ansprache (Phase 2, ADR-0048)

Zweite Vorgangsart: **`approach`**, der erste Kontakt einer Firma zu einer
Person, die ihr das Profil freigegeben hat.

- **Geöffnet wird sie nur über `POST /messages/approaches`**, mit mindestens
  der Rolle `recruiter`, und nur, wenn der Ledger `profile.visibility:*`
  bestätigt. Sonst 404 — gleich, ob es die Person gibt.
- **Eine Ansprache pro Paar Firma/Person.** Die Referenz wird aus beiden
  abgeleitet; eine zweite Ansprache landet im selben Gespräch.
- **Wer dazugehört, steht im Gespräch selbst.** Die Firmenseite fragt den
  Ledger bei jeder Anfrage erneut: wer sein Profil zurücknimmt, wird ab der
  nächsten Anfrage nicht mehr angeschrieben. Die Person liest weiter.
- **Den Text entwirft der KI-Recruiter im Scout** (`/scout/candidates/{id}/draft`,
  ohne Namen) und legt ihn ins Formular; abgeschickt wird er erst mit
  „Freigeben und senden“. `drafted` hält fest, woher die Worte kamen, und nur
  die eigene Seite sieht es.
- **Antworten in einer laufenden Ansprache darf jedes Mitglied** — das ist
  Tagesarbeit, kein erster Kontakt.

## Ergänzung 08.10.2026 — die Initiativbewerbung (Phase 3, VISION-FRAGEN B2)

Dritte Vorgangsart **`initiative`**: das Spiegelbild der Ansprache. Die
Person schreibt einer Firma von deren Karriereseite aus, ohne Inserat.

- **Senden ist Freigeben.** `POST /messages/initiatives` schreibt mit dem
  Token der Person `profile.visibility:tenant:<id>` in den Ledger — wie eine
  Bewerbung auf ein Inserat. Die Seite sagt es vor dem Knopf.
- **Reihenfolge:** erst den Text prüfen, dann die Mitglieder der Firma holen
  (keine Mitglieder → 404, keine Freigabe), dann freigeben, dann das Gespräch
  öffnen. Ein abgewiesener Text hinterlässt keine Freigabe, ein stummer
  Ledger kein Gespräch.
- **Zugriff wie bei der Ansprache:** die Firmenseite fragt den Ledger bei
  jeder Anfrage, die Person liest nach einem Widerruf weiter.
- Es ist der **einzige** Ort, an dem messages-service in den Ledger schreibt
  (`IReleaseWriter`).
