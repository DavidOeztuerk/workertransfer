# ADR-0042: assessment-service — die Bewertung gehört dem Vorgang, nicht dem Menschen

**Status:** angenommen (11.09.2026) · **gebaut am 11.09.2026**
**Betrifft:** assessment-service, consent-service, notification-service, identity-service (Kaskade), `src/gateway`, `web/`
**Verwandt:** ADR-0022 (kein Gesamtscore), ADR-0020 (Sichtbarkeit lebt im Ledger), ADR-0013 (Einwilligung wirkt sofort), ADR-0026 (Zahlen zählen Ereignisse, nicht Menschen), ADR-0025 (Postausgang, inhaltsfrei), ADR-0027 (Löschung), ADR-0036 (scout-service), ADR-0037 (advisor-service)

> **Zur Nummer:** PBI-5 und `docs/SESSIONS.md` verlangen „ADR-0040". Die war beim
> Schreiben schon vergeben (das Gateway liefert keine Oberfläche), 0041 ebenso
> (Entfernung). Dies ist dieselbe Entscheidung unter der nächsten freien Nummer.

## Warum es dieses ADR gibt

Dieser Dienst hat das größte Missbrauchspotenzial der ganzen Plattform, und es
sind zwei verschiedene Missbräuche, die man leicht für einen hält.

**Der erste ist die unbezahlte Arbeit.** Eine „Arbeitsprobe" ohne genannten
Umfang ist eine Aufgabe, deren Preis die Person erst kennt, wenn sie ihn bezahlt
hat. Wer drei Tage investiert und dann eine Absage bekommt, hat drei Tage
gearbeitet. Die Plattform, die das Formular dafür stellt, ist daran beteiligt.

**Der zweite ist die Note.** Eine Rückmeldung eines Unternehmens über die Arbeit
eines Menschen ist genau die Art von Aussage, die ADR-0022 aus dem Baum entfernt
hat — sobald sie den Vorgang verlässt. Sie muss es nur einmal tun: steht sie im
Profil, ist sie ein Zeugnis; steht sie in einer Suche, ist sie eine Rangliste;
sieht ein zweites Unternehmen sie, ist sie eine Auskunftei.

Der Anlass ist eine Messung am eigenen Baum, und sie fällt zugunsten dessen aus,
was heute steht — mit einer Lücke, die genau hier zuschlägt.

**Gemessen am 11.09.2026:** `Bewerbungsstand` kennt fünf Werte
(`Submitted`, `Reviewing`, `Rejected`, `Withdrawn`, `Hired`), und weder
`Bewerbung` noch `BewerbungV1` trägt **ein einziges Feld, in das ein
Unternehmen etwas an die Person schreibt**. Die Absage ist ein Zustandswort und
sonst nichts. Für eine Bewerbung ist das vertretbar: ein Unternehmen, das
begründen *muss*, begründet formelhaft, und ein Freitextfeld über einen
Menschen ist ein Freitextfeld über einen Menschen.

Für eine Arbeitsprobe ist es genau falsch herum. Dort hat die Person
**gearbeitet**, und eine Absage ohne ein Wort dazu ist der Tausch, den dieser
Dienst nicht vermitteln darf: Arbeit gegen Schweigen.

Die Messung nach der anderen Seite fällt ebenso deutlich aus. Der Baum kennt
heute genau eine Zahl, die etwas bewertet — `Sterne` an einer GitHub-Verbindung.
Sie steht dort, weil GitHub sie meldet, sie handelt von einem **Repository**,
und ihr Kommentar sagt „weitergegeben, nicht gerechnet". Über einen Menschen
gibt es im ganzen Baum keine. Das soll so bleiben, und dieser Dienst ist die
erste Stelle, an der jemand das plausibel anders machen könnte.

## Die Entscheidung

`assessment-service` wird gebaut, und er hält drei Zusagen. Jede ist als
Mechanismus umgesetzt und nicht als Absatz — eine Zusage, die nur in einer
Richtlinie steht, ist beim nächsten Feld weg.

### 1. Die Bewertung gehört dem Vorgang

Sie steht in keiner Suche, in keinem Profil, und kein zweites Unternehmen sieht
sie. Sie hat **keine Zahl** — keine Note, keine Sterne, keinen Prozentwert, kein
„3 von 5".

Der Mechanismus ist nicht ein Verbot, sondern eine **Abwesenheit**: es gibt
keinen Endpunkt, der Bewertungen über eine Person hinweg herausgibt, und keinen,
der über Unternehmensgrenzen liest. Der Bestand kennt zwei Fragen — „die Vorgänge
dieser Firma" und „meine Vorgänge" —, und die erste trägt den Mandanten aus dem
geprüften Token. Eine dritte Frage („die Vorgänge *dieser Person*", gestellt von
einer Firma) existiert nicht, und die geschlossene Endpunktmenge in
`AuflagenTests` macht ihr Hinzufügen zu einer sichtbaren Änderung.

**Die Alternative war eine Sichtbarkeitsfähigkeit im Ledger** — „meine
Bewertungen sind für Firma Y sichtbar". Verworfen, und zwar nicht aus Aufwand:
sobald es den Schalter gibt, gibt es den Druck, ihn umzulegen. Ein Unternehmen,
das im Erstkontakt fragt „gib doch deine bisherigen Bewertungen frei", hat einen
Menschen vor sich, der nein sagen kann und dabei weiß, was es kostet. Ein
Schalter, den man unter Druck betätigt, ist keine Einwilligung, und ein Zeugnis
mit Einwilligungsanstrich ist trotzdem ein Zeugnis.

### 2. Die Person sieht die Bewertung — immer

Auch bei Absage, auch nachdem sie ihre Sichtbarkeit widerrufen hat.

Drei Mechanismen, und der dritte ist der, auf den es ankommt:

- **Es gibt genau ein Bewertungsfeld**, und es ist dasselbe, das die Person
  liest. Kein internes Feld daneben, keine Notiz, kein „nur für uns". Wo es
  zwei gäbe, stünde im zweiten die Wahrheit.
- **`GET /assessments/{id}` antwortet beiden Seiten byte-gleich.** Nicht
  „ähnlich", nicht „dieselben Felder": dieselben Bytes, und ein Test vergleicht
  sie. Eine Firmensicht neben einer Personensicht wäre die Stelle, an der die
  beiden auseinanderlaufen — und zwar erst Monate später, beim nächsten Feld.
- **Der Ledger bewacht die Firmenseite und niemals die Personenseite.** Wer
  widerruft, verschwindet für das Unternehmen (404, wie überall) — aber liest
  weiter, was über ihre Arbeit geschrieben wurde. Eine Bewertung, aus der man
  sich aussperren kann, indem man Sichtbarkeit zurücknimmt, wäre eine
  Beurteilung, die der Beurteilte nie liest. Genau das wird hier nicht gebaut.

Und damit eine Absage nicht doch schweigend ergeht: **ein Ausgang ohne Text ist
422.** Ablehnen und Begründen sind ein Schritt, nicht zwei, und der zweite
lässt sich nicht weglassen.

**Die Bewertung wird einmal geschrieben.** Ein zweites Mal ist 409. Eine
Bewertung, die sich nachträglich ändern lässt, ist eine, die die Person gelesen
haben kann, bevor sie ihren endgültigen Wortlaut bekam — und was sie gelesen
hat, wäre dann nicht mehr nachweisbar. Wer sich vertan hat, schreibt einen
neuen Vorgang oder redet mit dem Menschen.

### 3. Der Umfang steht vorne, und Ablehnen wird nirgends vermerkt

`hours` ist **Pflicht** und liegt zwischen **1 und 8**. Die Obergrenze ist eine
Entscheidung und wird hier begründet: mehr als ein Arbeitstag ist keine Probe
mehr, sondern Arbeit, und für Arbeit gibt es einen Vertrag und kein Formular.
Die Plattform bietet für „16 Stunden" schlicht kein Feld an — wer so etwas will,
muss es außerhalb tun, und dann ist es sichtbar das, was es ist.

Die Frist liegt mindestens **48 Stunden** in der Zukunft. Der Grund ist
derselbe: eine Aufgabe für morgen früh misst nicht, wie jemand arbeitet, sondern
ob er gerade alles andere stehen lassen kann — und das hat mit Können nichts zu
tun und mit Lebensumständen alles. Es ist dieselbe Schieflage, die ADR-0022 an
GitHub beschreibt.

**Ablehnen ist keine Handlung, sondern das Ausbleiben einer.** Es gibt keine
Route, keinen Zustand und kein Feld dafür. Wer nicht will, tut nichts; die Frist
läuft ab, und der Vorgang steht auf `expired`. Dieser Zustand ist
**ununterscheidbar** von „hat es sich vorgenommen und nicht geschafft" und von
„war krank" — er sagt nur, dass nichts eingereicht wurde, und das ist eine
Aussage über den Vorgang.

**Die Alternative war ein höflicher Absageknopf.** Er wäre freundlicher zum
Unternehmen, das sonst wartet, und genau deshalb ist er verworfen: er erzeugt
den Vermerk, den diese Zusage verbietet. „Hat dreimal abgelehnt" ist eine
Tatsache über einen Menschen, sie entsteht aus lauter einzelnen berechtigten
Klicks, und sie ist danach da. Das Schweigen ist die Antwort, und die Frist
beendet es.

## Worauf eine Aufgabe steht

Auf dem Ledger, wie alles andere: `profile.visibility:tenant:<id>` oder
`profile.visibility:public`. Dieselbe Grundlage wie Stufe 1 eines Gesprächs
(ADR-0037), und **keine eigene Fähigkeit** — eine `assessment.*` wäre eine
zweite Wahrheit über dieselbe Frage, und sie liefe beim ersten Widerruf
auseinander.

**Keine neue Fähigkeit heißt auch: keine Kopplung an advisor-service.** Eine
Aufgabe folgt in der Praxis auf ein Gespräch, aber sie *hängt* nicht daran. Ein
Aufruf von hier nach dort machte aus zwei Diensten einen, und er machte die
Antwort auf „darf ich?" von der Erreichbarkeit eines dritten Dienstes abhängig.
Beide fragen den Ledger, und der Ledger ist die eine Stelle.

Verborgen, nicht vorhanden und nichts freigegeben antworten **404, byte-gleich
bis auf die Korrelationskennung** (ADR-0020 §1). Schweigt der Ledger, ist es
**503** und nicht 404: „wir wissen es nicht" ist etwas anderes als „gibt es
nicht", und aus einem Ausfall darf keine Aussage über einen Menschen werden.

## Die Lösung ist ein Verweis, keine Datei

Eingereicht wird **Text und höchstens eine Adresse**. Dieser Dienst nimmt keine
Bytes entgegen.

Der Grund ist nicht Sparsamkeit, sondern die Löschung: Dateien einer Person
liegen in portfolio-service und in resume-service, und beide räumen sie in der
Kaskade ab (ADR-0027, ADR-0035). Eine dritte Ablage wäre ein dritter Ort, den
eine Löschung erreichen muss — und der eine, den sie irgendwann nicht erreicht.

**Und die Adresse wird nie abgerufen.** Sie wird gespeichert und angezeigt, und
ein Mensch klickt sie. Ein Dienst, der sie holte, führte eine von einer Person
gewählte Adresse in eine Serveranfrage — und die Egress-Grenze wiese sie
ohnehin ab, weil sie in keiner Konfiguration steht. Das ist hier kein Hindernis,
sondern die richtige Antwort: was ein Fremder in ein Feld schreibt, ist kein
Ziel dieses Servers.

## Was dieses ADR nicht entscheidet

- **Ob eine Arbeitsprobe bezahlt wird.** Die Plattform kennt keine Zahlung
  (der Vermittlungsbetrag im transfer-service ist eine Zahl in einem Angebot,
  kein Zahlungsweg). Der Umfang steht vorne, damit die Person entscheiden kann
  — mehr kann ein Feld nicht leisten.
- **Ob die KI-Naht hier etwas zu tun hat.** Sie hätte genau eine plausible
  Aufgabe — die Aufgabenstellung entwerfen, als Spiegel von `POST /jobs/draft`
  (ADR-0024) —, und ausdrücklich niemals die *Bewertung*. Ein Modell, das die
  Arbeit eines Menschen beurteilt, ist die Black Box mit Komma aus ADR-0022,
  nur in Prosa. Gebaut ist keines von beidem; es gibt keinen `IEntwerfer`-Port
  in diesem Dienst, und `AuflagenTests` hält das fest.
- **Ob es eine Vorlagenbibliothek für Aufgaben gibt.** Wahrscheinlich nützlich,
  und sie wirft keine dieser Fragen auf.

## Löschung

Ab der ersten Tabelle ist assessment-service Löschempfänger. `"assessment"`
gehört in `Loeschempfaenger.Fremde` und in `LoeschempfaengerTests.Dienste`; die
Kaskade hat damit **elf** fremde Empfänger.

**Kein Aufbewahrungsfall, auch nicht für die Bewertung.** Was hier steht, ist
kein Beleg, der jemand anderem gehört — anders als eine eingestellte Bewerbung
oder ein bezahlter Transfer (ADR-0027 §3). Eine Bewertung, die die Löschung
überlebte, wäre das Zeugnis, das dieses ADR im ersten Abschnitt ausschließt,
nur mit besonders schlechtem Zeitpunkt.
