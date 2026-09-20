# Auftrag: Nachweis und KI-Pflichten

> **Ausgeführt am 20.09.2026 (PR #87), und der Baum steht seit demselben Tag auf
> Noelia 6.4.0 (ADR-0045).** Dieser Auftrag wird **nicht umgeschrieben** — er ist
> gefahren worden und ist Geschichte.
>
> Sein zweiter Satz entschied, *keinen Quelltext* aus Noelia zu übertragen, weil
> „WorkerTransfer auf Girder steht, nicht auf Noelia". Das war falsch: Girder und
> Noelia sind dieselbe Codelinie. Wäre der Umstieg zuerst gelaufen, wären seine
> **Phasen 3 bis 5** *„Noelias Dashboard komponieren und vier eigene Prüfungen
> schreiben"* gewesen.
>
> **Was er hervorgebracht hat, bleibt bezahlt:** die vier eigenen Prüfungen
> (`wt.ki.naht`, `wt.ki.keine-zahl`, `wt.einwilligung.wirkt`,
> `wt.loeschung.nachweis`) haben in Noelia keine Entsprechung, und die drei Funde
> — die Teilzeichenketten-Fehlalarme, die Zwei-Tabellen-Regel der Prüfspur, der
> Gegenversuch, der nicht fiel — gelten unverändert.
>
> **Was kollabiert, ist die Oberfläche**, und das steht noch aus: `ISecurityCheck`
> statt eigenem Läufer, `Noelia.Dashboard` statt der sieben `/nachweis/…`-Adressen,
> `OperatorReport` statt `bericht.json`, `RegulatoryReference` statt `Rechtsbezug`.

**Stand:** 20.09.2026 · **Zweig:** ab `develop` · **Nächstes ADR:** 0044

Dieser Auftrag überträgt ein Verfahren, das im Schwesterprojekt `Noelia` gebaut
und dort gegen einen echten Fremdverbraucher geprüft wurde, auf WorkerTransfer.
Er überträgt **keinen Quelltext** — WorkerTransfer steht auf Girder, nicht auf
Noelia. Übertragen wird die Methode und der Grund dafür.

---

## 1. Warum es diesen Auftrag gibt

WorkerTransfer hat die Prinzipien. Sie stehen in ADR-0022 (keine Zahl über einen
Menschen), ADR-0024 (die KI-Naht), ADR-0026 (Ereignisse zählen, nicht Menschen),
ADR-0027 (Löschung mit Nachweis), ADR-0033 (Beleg und Sichtbarkeit) — und im
Einwilligungsledger, der der Aktivposten dieses Produkts ist.

Was fehlt, ist an jeder Stelle dasselbe: **die Prinzipien sind aufgeschrieben und
nicht ausführbar.**

- `docs/KI-EINSATZ-PRUEFUNG.md` ist gründlich und ist eine **Momentaufnahme vom
  02.09.2026**. Sie sagt selbst, woran jedes Urteil hängt. Ein Urteil, das an
  einer Zeile hängt, hängt an der Zeile von damals.
- `Adr0022Tests` prüft die Feldmenge **zur Bauzeit**. Der KI-Zugang ist aber
  `KiZugangV1` — Anbieter, `base_url`, Modell und Schlüssel kommen **pro Person
  zur Laufzeit** aus den Kontoeinstellungen. Kein Test der Welt sieht, wohin
  dieser Baum heute Abend tatsächlich spricht.
- Die eigene Dokumentation von `Dienstgrundlage` beschreibt den Fehler wörtlich:
  *„Wir listeten fünf Module auf und bekamen fünf — und merkten nicht, was
  dadurch fehlte. Nichts davon war abgewählt; niemand hatte sie je gewählt."*

Das ist dieselbe Krankheit an drei Stellen: **vorhanden, aber nicht wirksam, und
nichts sagt es.** Noelia hat elf solche Fälle bei sich selbst gefunden, danach
neun weitere, und in der Sitzung, aus der dieser Auftrag stammt, noch einen.

### Was hier auf dem Spiel steht, und bei Noelia nicht

Noelias Demo ist eine Todo-Anwendung. Ihr KI-Befund ist eine Übung.

WorkerTransfer ist eine **Plattform für Bewerbung, Vermittlung und Wechsel** und
setzt Modelle in `scout-service` (die Suche nach Menschen), `assessment-service`
(die Arbeitsprobe mit einer Bewertung), `profile-service`, `jobs-service` und
`applications-service` ein.

Anhang III Nummer 4 Buchstabe a der Verordnung (EU) 2024/1689 nennt ausdrücklich
KI-Systeme, die *„für die Einstellung oder Auswahl natürlicher Personen"*
bestimmt sind, *„insbesondere um gezielte Stellenanzeigen zu schalten,
Bewerbungen zu analysieren und zu filtern und Bewerber zu bewerten."*

**Dieser Auftrag stuft nichts ein.** Ob ein bestimmter Dienst hier darunter
fällt, ist eine Beurteilung der *Nutzung* und gehört einem Menschen mit
juristischer Ausbildung. ADR-0022 ist erkennbar der Versuch, gar nicht erst
hineinzugeraten — ob er trägt, entscheidet dieser Mensch und nicht dieses
Repositorium.

Was dieser Auftrag baut, ist **das, was dieser Mensch braucht, um die Frage zu
beantworten**: ein vollständiges, datiertes, maschinell erzeugtes Verzeichnis
dessen, was tatsächlich läuft — und daneben, in derselben Zeile, was es nicht
beantwortet.

### Und was es wert ist

Ein Kunde dieser Plattform ist ein Arbeitgeber. Bevor dort ein technisches System
eingeführt wird, das geeignet ist, Verhalten oder Leistung von Beschäftigten zu
überwachen, redet der Betriebsrat mit (§ 87 Abs. 1 Nr. 6 BetrVG). Dieser
Betriebsrat bekommt heute eine Verkaufsbroschüre.

Er könnte ein datiertes, signiertes Dokument bekommen, das sagt, welche Modelle
angesprochen werden, was über eine Person gespeichert wird, was nachweislich
**nicht** berechnet wird — und was das Dokument offenlässt. Das ist kein
Nebenprodukt der Compliance. Das ist ein Verkaufsargument, das kein Wettbewerber
hat, weil er es nicht mit einem Screenshot herstellen kann.

---

## 2. Die Linie

> **Belege, keine Konformität.**

Ein Programm kann zeigen, dass etwas **vorhanden** ist, **wann** es entstanden
ist und dass es **unverändert** ist. Es kann **Angemessenheit** nicht zeigen.

Kein Artefakt aus diesem Auftrag sagt „konform", „zertifiziert", „erfüllt
Art. X". Nach Art. 42/43 DSGVO darf nur eine Aufsichtsbehörde oder eine nach
EN ISO/IEC 17065 akkreditierte Stelle zertifizieren; „zertifiziert" ohne
Akkreditierung ist in der EU eine irreführende Geschäftspraxis (RL 2005/29/EG,
RL 2006/114/EG). Die Fertigkeit `wt-nachweis` hält die Wortliste.

Jedes Zitat trägt ein Feld, das nennt, **was ein Mensch danach noch entscheidet**,
und dieses Feld ist nie leer.

---

## 3. Was gebaut wird

Fünf Phasen. Jede ist für sich abnehmbar und hinterlässt den Baum grün.

### Phase 1 — Prüfungen, die laufen

**Neu:** `src/shared/WorkerTransfer.Nachweis/` (ein achtes Ding unter `shared/`,
und dieser Auftrag begründet, warum es eines sein darf: es ist kein Domänenmodell
und keine Entscheidung, sondern die Form, in der jeder Dienst über sich selbst
Auskunft gibt).

```csharp
public interface IPruefung
{
    string Id { get; }                 // "wt.ki.anbieter", punktiert, stabil
    Bereich Bereich { get; }           // Einwilligung | KI | Loeschung | Ledger | Grenze
    IReadOnlyList<Rechtsbezug> Bezuege => [];
    Task<Befund> LaufenAsync(CancellationToken ct = default);
}

public sealed record Befund(
    string Id, Bereich Bereich, Stand Stand,   // Erfuellt | Hinweis | Fehlt | NichtAnwendbar
    string Zusammenfassung,                    // nur Gestalten, nie Werte
    string Abhilfe)
{
    public IReadOnlyList<Rechtsbezug> Bezuege { get; init; } = [];
}
```

**Regeln, die nicht verhandelbar sind:**

- `Zusammenfassung` und `Abhilfe` nennen **Gestalten und Handlungen, nie Werte.**
  Kein Schlüssel, kein Token, keine Verbindungszeichenfolge, kein roher
  Ausnahmetext. Der Läufer fängt Ausnahmen und setzt einen festen Satz ein —
  Anbieter schreiben Endpunkte in Ausnahmen.
- Eine Prüfung, deren Gegenstand fehlt, meldet `NichtAnwendbar`, nicht `Erfuellt`.
  Ein grüner Haken an etwas, das gar nicht gilt, ist Rauschen in genau dem
  Dokument, das Rauschen durchschneiden soll.
- **Kein Test darf bestehen, wenn man den Rumpf der Prüfung löscht.** In Noelia
  standen drei `…_ReturnsSelf`-Tests grün über auskommentierten Methoden.

**Angehängt an `Dienstgrundlage`**, weil das der eine Aufruf ist, den jeder
Dienst macht — und weil dieselbe Datei den Fehler dokumentiert, den das hier
verhindert.

Die erste Fuhre, je Dienst nur, was dort zutrifft:

| Prüfung | Was sie beantwortet |
|---|---|
| `wt.ki.anbieter` | Welche KI-Anbieter sind in dieser Instanz **tatsächlich** in Gebrauch — als Anbieter-Etikett und Host, aggregiert über Menschen, **nie pro Person** |
| `wt.ki.naht` | Trägt der Kontext, der das Modell verlässt, mehr als die vier vereinbarten Felder? Die Feldmenge festgenagelt, wie ADR-0024 §3 es verlangt und der .NET-Baum es bis heute schuldig bleibt |
| `wt.ki.keine-zahl` | Existiert in Domäne und Verträgen ein Feld, das eine Zahl über einen Menschen trägt? Der Laufzeit-Zwilling von `Adr0022Tests` |
| `wt.ki.protokoll` | Wird jeder Modellaufruf automatisch aufgezeichnet, und lässt sich die Aufzeichnung als unverändert zeigen? |
| `wt.einwilligung.wirkt` | Wirkt ein Widerruf beim nächsten Zugriff — geprüft, nicht behauptet |
| `wt.loeschung.nachweis` | Hinterlässt eine Löschung den Nachweis, den ADR-0027 verlangt? |
| `wt.grenze.ziele` | Welche Hosts spricht dieser Dienst an, und wessen Recht gilt dort |

**Der KI-Anbieter-Befund ist hier stärker als bei Noelia.** Noelia muss aus
Hostnamen raten und sagt deshalb, das Verzeichnis sei eine Untergrenze.
WorkerTransfer *hält* die `base_url` in `KiZugangV1` — das Verzeichnis kann
vollständig sein.

> **Und genau deshalb ist es personenbezogen.** Der Befund nennt Anbieter und
> Host und **zählt** — „drei Menschen auf `api.anthropic.com`, einer auf einem
> eigenen Server". Nie, wer. Wer welchen Anbieter benutzt, ist eine Aussage über
> eine Person und gehört nicht in ein Dokument, das jemand herumreicht.
> ADR-0026 sagt dasselbe für Ereignisse.

### Phase 2 — Rechtsbezüge

```csharp
public sealed record Rechtsbezug(
    Regelwerk Regelwerk,      // Dsgvo | KiVo | Nis2 | Dora | BetrVG
    string Artikel,           // "Art. 30 Abs. 1 Buchst. d, e"
    string Pflicht,           // wonach der Artikel fragt
    string Leser);            // was ein Mensch danach noch entscheidet — NIE leer
```

`Rechtsbezuege.Alle` sammelt sie an einer Stelle, weil derselbe Artikel von
mehreren Prüfungen belegt wird und ein Zitat, das zwischen ihnen auseinanderläuft,
schlimmer ist als keines: der Leser kann dann nicht mehr sagen, ob zwei Befunde
von einer Pflicht handeln oder von zweien.

Aufzunehmen, mindestens:

| Regelwerk | Artikel | Wofür der Beleg hier taugt |
|---|---|---|
| DSGVO | Art. 30 Abs. 1 | Empfängerverzeichnis — die KI-Anbieter sind Empfänger |
| DSGVO | Kap. V (Art. 44–49) | Drittlandübermittlung, sobald ein Modell außerhalb der Union antwortet |
| DSGVO | Art. 28 Abs. 3 | Auftragsverarbeitung — besteht ein AV-Vertrag mit dem Anbieter |
| DSGVO | Art. 22 | Automatisierte Entscheidung im Einzelfall — **der Artikel, um den ADR-0022 herumbaut** |
| DSGVO | Art. 17, Art. 5 Abs. 2 | Löschung und Rechenschaft |
| KI-VO | Art. 12, Art. 26 Abs. 6 | Automatische Aufzeichnung, Aufbewahrung ≥ 6 Monate — **bindet ab 02.12.2027** |
| KI-VO | Art. 26 | Pflichten des Betreibers — bindet ab 02.12.2027 |
| KI-VO | Art. 50 | Transparenz gegenüber der Person — **seit 02.08.2026 in Kraft** |
| KI-VO | Anhang III Nr. 4 | Beschäftigung — die Einstufungsfrage, die dieses Repositorium **nicht** beantwortet |
| BetrVG | § 87 Abs. 1 Nr. 6 | Mitbestimmung bei technischen Überwachungseinrichtungen |

**Ein Datum, wo eines gilt.** Ein Dokument, das eine Pflicht von 2027 so
darstellt, als binde sie heute, lädt den Leser ein, zu früh Geld auszugeben.

### Phase 3 — Die Sicht, getrennt nach Abschnitten

**Nicht am Gateway.** ADR-0040 hat entschieden, dass das Gateway eine API-Tür ist
und keine Oberfläche liefert; dieser Auftrag hebt das nicht auf.

Jeder Dienst liefert unter seinem eigenen Port:

```
GET /nachweis                 Übersicht: eine Zeile je Abschnitt
GET /nachweis/ki              Modelle, Anbieter, Naht, Protokoll
GET /nachweis/einwilligung    Ledger, Widerruf, Sichtbarkeit
GET /nachweis/loeschung       Kaskade und Nachweis
GET /nachweis/grenze          Ziele und Jurisdiktion
GET /nachweis/pflichten       Beobachtung → Artikel → was offenbleibt
GET /nachweis/bericht.json    dieselbe Lesung als Daten
```

- **Eine Seite je Abschnitt, mit eigener Adresse.** Ein Abschnitt, den man
  verlinken kann, landet im Ticket bei dem, der handeln muss. Eine Seite mit
  sieben Abschnitten wird überflogen.
- Ein Pfad darunter, der keinen Abschnitt benennt, antwortet **404** — nicht die
  Übersicht unter falscher Adresse.
- Hinter derselben Tür wie alles andere. Ohne Berechtigung **404**, nicht 403:
  eine Betriebsoberfläche, deren Existenz man erraten kann, ist eine Auskunft.
- **Nur Gestalten.** Kein Wert, kein Schlüssel, kein Name einer Person.
- `bericht.json` und die Seite kommen aus **einer** Lesung. Zwei Abfragepfade zu
  einer Aussage laufen auseinander, und beim ersten Mal merkt es niemand.

Die Pflichten-Seite hat vier Spalten, und die vierte ist die, die sie ehrlich
macht: *Artikel · wonach er fragt · was hier dafür spricht · was Sie noch
entscheiden.* **Keine Urteilsspalte.** Ein Artikel ist nichts, was eine Prüfung
bestehen kann.

### Phase 4 — Der Nachweis als Dokument

Ein `make nachweis` sammelt die Berichte aller Dienste gegen den laufenden Stapel
und schreibt **drei** getrennte, datierte, signierte Dokumente:

| Dokument | Frage | Leser |
|---|---|---|
| **Datenschutz** | Welche Empfänger, welches Drittland, was wird über eine Person gespeichert | Datenschutzbeauftragter |
| **KI** | Welche Modelle, welcher Kontext verlässt das Haus, was wird aufgezeichnet — und was ausdrücklich nicht berechnet wird | Wer die Anhang-III-Frage beantwortet |
| **Mitbestimmung** | Was das System über Beschäftigte erfassen kann und was nicht | Betriebsrat |

Drei statt eines, weil sie von drei Menschen für drei Zwecke gelesen werden und
ein gemischtes Dokument niemand vertritt — und weil man aus einem gemischten
Dokument den einen Teil zitieren kann, ohne den Teil zu zitieren, der ihn
einschränkt.

- **ECDSA P-256 mit SHA-256**, Signatur über die kanonische Form (Schlüssel
  sortiert, keine bedeutungslosen Leerzeichen), damit ein Prüfer sie nachrechnen
  kann.
- Der **Geltungssatz steht innerhalb der Signatur.** Ein Umfang, der außerhalb
  steht, ist ein Umfang, den jemand umformulieren kann.
- Die Vorbehalte werden **aus der Lesung abgeleitet**, nicht als Baustein
  angehängt. Eine feste Liste liest man einmal.
- Jedes Dokument sagt in seinem eigenen signierten Text, dass es maschinell
  erzeugte technische Evidenz ist, kein Zertifikat, und dass keine akkreditierte
  Stelle es beurteilt hat.

### Phase 5 — Das Tor

```bash
make nachweis          # Dokumente gegen den laufenden Stapel
make nachweis-pruefen  # jede Prüfung in jedem Dienst; rot bei Fehlt
```

`make nachweis-pruefen` geht in `make validate`. Ein Tor, das man aufrufen muss,
um es zu haben, hat man nicht.

**Und ein Gegenversuch, der scheitern muss:** ein Test, der ein Feld `passung`
in einen Vertrag einführt und beweist, dass `wt.ki.keine-zahl` darauf rot wird.
Eine Prüfung, die nie rot war, ist keine.

---

## 4. Was ausdrücklich **nicht** gebaut wird

- **Keine Einstufung nach Anhang III.** Das Dokument stellt die Frage und legt
  die Belege daneben. Beantwortet wird sie von einem Menschen.
- **Kein Urteil über einen Vertrag.** Ob ein AV-Vertrag taugt, ob eine
  Übermittlungsgarantie trägt, steht in einem Aktenschrank.
- **Keine Aussage über Inhalte.** Der Nachweis nennt Empfänger, nie was gesendet
  wurde. Ob personenbezogene Daten im Prompt stehen, sieht von außen niemand.
- **Keine Zahl über einen Menschen**, auch nicht als Nebenprodukt einer Prüfung.
  ADR-0022 gilt auch für dieses Werkzeug.
- **Keine Oberfläche am Gateway.** ADR-0040.
- **Kein neuer Dienst.** Eine gemeinsame Bibliothek und je ein Endpunkt.

---

## 5. Was am Ende wahr sein muss

- [ ] `make build` — 0 Warnungen
- [ ] `./scripts/test-dotnet.sh` — jede Reihe grün, Zahl auf dem Schirm
- [ ] `make nachweis-pruefen` grün, und der Gegenversuch rot
- [ ] Jeder Dienst mit KI hat `/nachweis/ki`; jeder Dienst hat `/nachweis`
- [ ] Ein Pfad ohne Abschnitt: 404. Ohne Berechtigung: 404
- [ ] Kein Wert, kein Schlüssel, kein Name in Seite oder `bericht.json` —
      mit einem Kanarienvogel geprüft
- [ ] Die drei Dokumente verifizieren; eine geänderte Kopie verifiziert nicht
- [ ] **Kein `Rechtsbezug` mit leerem `Leser`** — als Test
- [ ] Die Wörter „zertifiziert", „Zertifikat", „konform" kommen in keinem
      Geltungssatz und keinem Vorbehalt vor — als Test
- [ ] ADR-0044 steht und begründet die achte Sache unter `shared/`
- [ ] `docs/KI-EINSATZ-PRUEFUNG.md` trägt oben einen Verweis darauf, welche
      ihrer Urteile jetzt laufend geprüft werden — **die Datei wird nicht
      umgeschrieben.** Sie ist eine Momentaufnahme, und eine Momentaufnahme zu
      ändern hieße, sie zu fälschen
- [ ] `CLAUDE.md` und `README.md` nennen `make nachweis`

---

## 6. Reihenfolge

Phase 1 zuerst und allein, gegen **einen** Dienst — `profile-service`, weil
seine KI-Naht die am besten beschriebene ist. Erst wenn dort eine Prüfung läuft,
rot werden kann und im Bericht steht, wird sie auf die anderen gezogen.

Ein Auftrag, der in vierzehn Diensten gleichzeitig anfängt, ist vierzehnmal die
Gelegenheit, dieselbe Entscheidung anders zu treffen.

---

## 7. Was offenbleibt, und bewusst

**Die Grenze ist hier schwerer als bei Noelia.** Dort sind die Ziele vor dem
Start deklariert, und ein nicht deklarierter Aufruf scheitert — deshalb ist das
Register dort vollständig statt beobachtet. Hier wählt die **Person** ihren
Anbieter zur Laufzeit. Ein Verzeichnis kann sagen, was heute eingetragen ist; es
kann nicht sagen, was morgen eingetragen wird.

Ob daraus eine Liste erlaubter Anbieter folgen soll, ist eine **Produktfrage** —
sie nimmt der Person eine Wahl, die ihr das Produkt bisher ausdrücklich lässt.
Dieser Auftrag entscheidet sie nicht. Er macht sie sichtbar, und das ist der
Zustand, in dem sie entschieden werden kann.
