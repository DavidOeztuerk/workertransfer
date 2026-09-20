# ADR-0044 — Der Nachweis: Belege, keine Konformität

**Datum:** 20.09.2026 · **Status:** angenommen

## Zusammenhang

WorkerTransfer hat die Prinzipien. Sie stehen in ADR-0022 (keine Zahl über
einen Menschen), ADR-0024 (die KI-Naht), ADR-0026 (Ereignisse zählen, nicht
Menschen), ADR-0027 (Löschung mit Nachweis), ADR-0033 (Beleg und Sichtbarkeit) —
und im Einwilligungsledger, der der Aktivposten dieses Produkts ist.

Was fehlte, ist an jeder Stelle dasselbe: **die Prinzipien sind aufgeschrieben
und nicht ausführbar.** Drei Belege dafür, und alle drei sind gemessen:

- `docs/KI-EINSATZ-PRUEFUNG.md` ist gründlich und ist eine **Momentaufnahme vom
  02.09.2026**. Sie sagt selbst, woran jedes Urteil hängt. Ein Urteil, das an
  einer Zeile hängt, hängt an der Zeile von damals — und sie zählt zwei
  KI-Verbraucher, wo es heute **vier** sind (`profile`, `jobs`, `scout`,
  `applications`).
- `Adr0022Tests` und `EntwurfsgrenzeTests` prüfen die Feldmengen **zur
  Bauzeit**. Der KI-Zugang ist aber `KiZugangV1` — Anbieter, `base_url`, Modell
  und Schlüssel kommen **pro Person zur Laufzeit** aus den Kontoeinstellungen.
  Kein Test der Welt sieht, wohin dieser Baum heute Abend tatsächlich spricht.
- Die eigene Dokumentation von `Dienstgrundlage` beschreibt den Fehler wörtlich:
  *„Wir listeten fünf Module auf und bekamen fünf — und merkten nicht, was
  dadurch fehlte. Nichts davon war abgewählt; niemand hatte sie je gewählt."*

Dazu kommt, was hier auf dem Spiel steht und bei einer Todo-Anwendung nicht:
diese Plattform setzt Modelle in `scout-service`, `assessment-service`,
`profile-service`, `jobs-service` und `applications-service` ein. Anhang III
Nr. 4 Buchst. a der Verordnung (EU) 2024/1689 nennt ausdrücklich KI-Systeme,
die *„für die Einstellung oder Auswahl natürlicher Personen"* bestimmt sind.

Und ein Kunde dieser Plattform ist ein Arbeitgeber. Bevor dort ein technisches
System eingeführt wird, das geeignet ist, Verhalten oder Leistung von
Beschäftigten zu überwachen, redet der Betriebsrat mit (§ 87 Abs. 1 Nr. 6
BetrVG). Dieser Betriebsrat bekam bisher eine Verkaufsbroschüre.

## Entscheidung

### 1. Die Linie: Belege, keine Konformität

Ein Programm kann zeigen, dass etwas **vorhanden** ist, **wann** es entstanden
ist und dass es **unverändert** ist. Es kann **Angemessenheit** nicht zeigen.

Kein Artefakt aus diesem Werk sagt „konform", „zertifiziert" oder „erfüllt
Art. X". Nach Art. 42/43 DSGVO darf nur eine Aufsichtsbehörde oder eine nach
EN ISO/IEC 17065 akkreditierte Stelle zertifizieren; „zertifiziert" ohne
Akkreditierung ist in der EU eine irreführende Geschäftspraxis (RL 2005/29/EG
gegenüber Verbrauchern, RL 2006/114/EG zwischen Unternehmen). Das ist ein echtes
Risiko, kein theoretisches — und deshalb ein **Test** und keine Durchsicht:
`DokumentwortTests` liest die Geltungssätze aus dem Erzeuger,
`scripts/nachweis_worte.py` prüft, was wirklich herauskam.

**Jedes Zitat trägt ein Feld, das nennt, was ein Mensch danach noch
entscheidet** (`Rechtsbezug.Leser`), und dieses Feld ist nie leer — als Test.
Ein Zitat, das seinen eigenen Artikel erledigte, wäre genau die Anmaßung, gegen
die das Vokabular existiert.

### 2. Eine achte Sache unter `shared/`, und warum sie eine sein darf

`src/shared/WorkerTransfer.Nachweis/` ist das achte Ding unter `shared/`. Die
Sharing-Regel lässt nur Domänenneutrales, Transportloses und Fachfreies zu —
und dieses Paket ist alles drei: es ist **kein Domänenmodell und keine
Entscheidung**, sondern die **Form, in der jeder Dienst über sich selbst
Auskunft gibt**. Es zieht zwei Fremdpakete (`Configuration.Abstractions`,
`DependencyInjection.Abstractions`), sieht in keine Datenbank und spricht kein
HTTP. Was eine Prüfung an ihrem Gegenstand ablesen muss, liest der Dienst und
reicht es herein.

Angehängt ist es an `Dienstgrundlage`, weil das der eine Aufruf ist, den jeder
Dienst macht — und weil dieselbe Datei den Fehler dokumentiert, den das hier
verhindert.

**Was eine Entscheidung ist, bleibt im Verbundpunkt des Dienstes.** Welche vier
Felder zum Modell hinausgehen dürfen, ist ADR-0024 §3 für genau diesen
Verbraucher; eine gemeinsame Menge wäre der erste Schritt zu einem gemeinsamen
Prompt mit einem `if`. Genau eine Prüfung steht zentral (`wt.grenze.ziele`), und
zwar weil sie nur die Konfiguration liest und damit für jeden Dienst dieselbe
ist — sie ist die eine, die ein neuer Dienst nicht vergessen kann.

### 3. `NichtAnwendbar` statt `Erfuellt`, wo der Gegenstand fehlt

Eine Prüfung, deren Gegenstand fehlt, meldet `NichtAnwendbar`. Ein grüner Haken
an etwas, das gar nicht gilt, ist Rauschen in genau dem Dokument, das Rauschen
durchschneiden soll — und er addiert sich: vierzehn Dienste ohne KI-Naht ergäben
vierzehn grüne Haken über eine Naht, die es nicht gibt.

**`Hinweis` ist kein Mangel.** Dass Modellaufrufe nicht aufgezeichnet werden,
ist ADR-0024 und eine Entscheidung; ein Tor, das darauf rot geht, schaltet der
nächste Mensch ab. Rot wird nur `Fehlt` — eine Zusage dieses Baumes, die nicht
eingelöst ist.

### 4. Das KI-Verzeichnis zählt Menschen und nennt keinen

WorkerTransfer *hält* die `base_url` in `KiZugangV1`, das Verzeichnis kann
deshalb vollständig sein — und genau deshalb ist es personenbezogen. Der Befund
nennt Anbieter und Host und **zählt**: „drei Menschen auf `api.anthropic.com`".
Nie, wer. ADR-0026 sagt dasselbe für Ereignisse. Die Abfrage in
`EfAnbieterquelle` gruppiert und holt den verschlüsselten Schlüssel nicht
einmal aus der Datenbank — was nicht geholt wird, kann nicht versehentlich in
einen Befund geraten.

### 5. Die Seiten stehen am Dienst, nicht am Gateway

ADR-0040 hat entschieden, dass das Gateway eine API-Tür ist und keine
Oberfläche liefert; dieser Nachweis hebt das nicht auf. Jeder Dienst antwortet
unter seinem eigenen Hafen auf sieben Adressen: `/nachweis`, vier
Abschnittsseiten, `/nachweis/pflichten` und `/nachweis/bericht.json`.

**Eine Seite je Abschnitt, mit eigener Adresse** — ein Abschnitt, den man
verlinken kann, landet im Ticket bei dem, der handeln muss. **Ein Pfad darunter,
der keinen Abschnitt benennt, antwortet 404** und nicht die Übersicht unter
falscher Adresse.

**Ohne Berechtigung 404, nicht 403.** Eine Betriebsoberfläche, deren Existenz
man erraten kann, ist selbst schon eine Auskunft. Die Tür hat ihr **eigenes
Papier** (`Nachweis__Geheimnis`), ausdrücklich nicht das der Benachrichtigung
und nicht das der Löschung: „darf eine Mail anstoßen", „darf alles über einen
Menschen löschen" und „darf sehen, welche Anbieter diese Instanz benutzt" sind
drei verschiedene Rechte. **Leer heißt zu.**

**Die Seite und `bericht.json` kommen aus EINER Lesung.** Zwei Abfragepfade zu
einer Aussage laufen auseinander, und beim ersten Mal merkt es niemand.

Die Pflichtenseite hat vier Spalten — *Artikel · wonach er fragt · was hier
dafür spricht · was Sie noch entscheiden* — und **keine Urteilsspalte**. Ein
Artikel ist nichts, was eine Prüfung bestehen kann.

### 6. Drei Dokumente, nicht eines

`make nachweis` schreibt **Datenschutz**, **KI** und **Mitbestimmung** getrennt.
Drei, weil sie von drei Menschen für drei Zwecke gelesen werden und ein
gemischtes Dokument niemand vertritt — und weil man aus einem gemischten
Dokument den einen Teil zitieren kann, ohne den Teil zu zitieren, der ihn
einschränkt.

- **ECDSA P-256 mit SHA-256**, Signatur über die kanonische Form (sortierte
  Schlüssel, keine bedeutungslosen Leerzeichen), damit ein Prüfer sie
  nachrechnen kann. Die Signaturdatei nennt die Form als Einzeiler.
- **Der Geltungssatz steht innerhalb der Signatur.** Ein Umfang, der außerhalb
  steht, ist ein Umfang, den jemand umformulieren kann.
- **Die Vorbehalte werden aus der Lesung abgeleitet**, nicht als Baustein
  angehängt. Eine feste Liste liest man einmal.
- **Wer nicht antwortete, steht namentlich im Dokument.** Ein Dienst, der
  schweigt, darf nicht wie einer aussehen, der nichts zu melden hat.
- Jedes Dokument sagt in seinem eigenen **signierten** Text, dass es maschinell
  erzeugte technische Evidenz ist, kein Zertifikat, und dass keine akkreditierte
  Stelle es beurteilt hat.

### 7. Das Tor braucht den Stapel, und das wird gesagt statt verschwiegen

`make nachweis-pruefen` fährt jede Prüfung in jedem Dienst und geht rot, sobald
eine Zusage nicht eingelöst ist. Es steht in `make validate` — läuft dort aber
nur, wenn der Stapel antwortet, und **wenn nicht, sagt der Bericht das
namentlich**. Dieselbe Behandlung, die `validate.sh` den E2E-Reisen gibt: ein
Haken über einem Lauf, der nicht stattfand, ist die Sorte grün, gegen die dieses
Skript gebaut ist.

Das ist kein Kompromiss, sondern die Folge dessen, was hier gemessen wird: was
dieser Behälter heute Abend wirklich tut. Das steht in keiner Assembly.

## Was ausdrücklich nicht gebaut wird

- **Keine Einstufung nach Anhang III.** Das Dokument stellt die Frage und legt
  die Belege daneben. Beantwortet wird sie von einem Menschen mit juristischer
  Ausbildung. *Es steht nirgends, dass WorkerTransfer nicht hochriskant ist.*
- **Kein Urteil über einen Vertrag.** Ob ein AV-Vertrag taugt, ob eine
  Übermittlungsgarantie trägt, steht in einem Aktenschrank.
- **Keine Aussage über Inhalte.** Der Nachweis nennt Empfänger, nie was gesendet
  wurde. Ob personenbezogene Daten im Prompt stehen, sieht von außen niemand.
- **Keine Zahl über einen Menschen**, auch nicht als Nebenprodukt. ADR-0022 gilt
  auch für das Werkzeug, das ADR-0022 prüft.
- **Keine Oberfläche am Gateway** (ADR-0040). **Kein neuer Dienst.**
- **Kein Eintrag `Souveraenitaet` in `Regelwerk`.** Keine Verordnung verlangt
  digitale Souveränität; sie ist eine Entscheidung des Betreibers. Ein Eintrag
  machte aus einer Haltung eine Pflicht, die niemand geschrieben hat.

## Folgen

**Gemessen am 20.09.2026 gegen den laufenden Stapel:** 14 von 14 Diensten
gelesen, **83 Befunde** gefahren, keine Zusage unbelegt. Die drei Dokumente
verifizieren; eine geänderte Kopie verifiziert nicht.

**Zwei Dinge hat der erste Lauf gefunden, die kein Test gesehen hätte** — und
beide sind der Grund, warum es diesen Nachweis gibt:

1. **Die Wortsuche über Teilzeichenketten meldete drei Fehlalarme über
   vollkommen korrekten Code:** `Capability` (der Kerntyp des Ledgers) wegen
   `ability`, `Benefits` wegen `fit`, `Availability` wegen `ability`. Verglichen
   wird seitdem je **Silbe und am Silbenanfang** — derselbe Fehler, den Girder
   4.3.0 in seiner Maskierung hatte und 4.4.0 mit derselben Bewegung behob. Was
   die Silbenregel *nicht* findet, steht als Test daneben: ein Kompositum mit dem
   Wort am Ende (`Trefferanzahl`) entgeht ihr, und wer sich darauf verlässt,
   verlässt sich auf zu wenig.
2. **Die Ledger-Prüfung meldete `audit_events.TenantId`** — und lag falsch. Die
   Prüfspur hält fest, wer gehandelt hat und in welcher Eigenschaft (ADR-0012);
   dass dort ein Mandant steht, ist ihre Aussage und nicht ihr Fehler. Der
   *Ledger* hält die Einwilligung selbst, und die gehört der Person. Zwei
   Tabellen, zwei Regeln.

**Ein Feld wurde umbenannt:** `Seitenanfrage.Anzahl` heißt in profile-service
jetzt `Seitenlaenge` — genau wie in scout-service, und aus demselben Grund. Der
Wortschatz hat die Benennung erzwungen, und das ist seine Arbeit.

**Dreizehn Ausnahmen gibt es im ganzen Baum**, in drei Diensten, für drei
Homonyme: `Note` (englisch: Anmerkung), `Quote` (englisch: Zitat) und
`WorkloadPercent`/`PensumProzent` (das Pensum, das eine Person selbst nennt).
Jede trägt ihren Grund, und dieser Grund **steht im Befund** — wer eine Ausnahme
hinzufügt, schreibt sie in ein Dokument, das ein Betriebsrat liest. Eine stille
Liste in einem Testprojekt wächst; eine, die gelesen wird, nicht.

**Was offenbleibt, und bewusst:** Die Person wählt ihren KI-Anbieter zur
Laufzeit. Ein Verzeichnis kann sagen, was heute eingetragen ist; es kann nicht
sagen, was morgen eingetragen wird. Ob daraus eine **Liste erlaubter Anbieter**
folgen soll, ist eine Produktfrage — sie nimmt der Person eine Wahl, die ihr das
Produkt bisher ausdrücklich lässt. Dieses ADR entscheidet sie nicht. Es macht
sie sichtbar, und das ist der Zustand, in dem sie entschieden werden kann.
