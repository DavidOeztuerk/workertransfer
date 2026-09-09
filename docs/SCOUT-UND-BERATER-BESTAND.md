# Scout und Berater — was schon dasteht

Die Bestandsaufnahme vor den ADRs. [`SCOUT-UND-BERATER.md`](SCOUT-UND-BERATER.md)
ist ein **Entwurf** und beschreibt, was entstehen soll; dieses Dokument ist eine
**Messung** und beschreibt, was heute läuft. Wo beide sich widersprechen, gewinnt
die Messung — der Entwurf ist älter als der Code.

Gemessen am Stand vom 04.09.2026, am laufenden Stapel und im Quellbaum.

---

## Der wichtigste Fund: `/me/scouting` verlangt genau die Tabelle, die wir abgelehnt haben

Der Entwurf nennt `GET /me/scouting` — *„Du bist am 3. März in einer Suche von
Firma X aufgetaucht."* — und begründet damit den ganzen Dienst:

> Das ist der Unterschied zwischen einem Scout und einer Datenbank mit Noten —
> und es ist der Grund, warum das hier gebaut werden darf.

`resume-service` hat diese Frage schon entschieden, und zwar andersherum.
`Pruefhandlung` (Domäne, Prüfspur) sagt im Kommentar:

> Only changes of state. A *read* is not in here, and that is a decision:
> recording every time a company looked at a résumé would build a record of who
> looked at whom, which nothing in this system reads and which would be the most
> sensitive table in it.

**Beide Sätze stimmen, und sie schliessen einander nicht aus** — der Unterschied
steckt im Nebensatz *„which nothing in this system reads"*. Genau das kippt beim
Scout: die beobachtete Person liest sie. Eine Tabelle, die niemand liest, ist
reines Risiko; eine, die der Betroffene liest, ist Auskunft.

Das ist trotzdem eine Entscheidung und kein Nebenprodukt. **Sie gehört in das
ADR, ausdrücklich, mit der Prüfspur als Gegenstimme.** Drei Wege:

| | Was die Person sieht | Preis |
|---|---|---|
| **A — je Treffer** (der Entwurf) | „am 3.3. in einer Suche von Firma X" | Die sensibelste Tabelle des Systems entsteht. Sie sagt auch etwas über *Unternehmen*: wonach sie suchen |
| **B — verdichtet** | „diesen Monat in 3 Suchen aufgetaucht" | Weniger Auskunft, deutlich weniger Tabelle |
| **C — gar nicht** | nichts | Der Entwurf verliert seine eigene Begründung |

C scheidet damit aus. Meine Empfehlung ist **A**, aber mit zwei Auflagen, die
die Prüfspur uns beibringt: keine Freitextspalte, und der Eintrag gehört der
Person — er wird mit ihr gelöscht und ist für das Unternehmen nicht lesbar.

---

## Was es schon gibt, unter anderem Namen

### `/candidates` ist bereits der halbe Scout

`profile-service` (`ProfilEndpoints.cs`) hat `GET /candidates`, und es erfüllt
zwei der vier Auflagen des Entwurfs schon heute:

| Auflage des Entwurfs | heute |
|---|---|
| Nur für Unternehmen | **ja** — als Person 403 „no active company" |
| Filter über genannte Fähigkeiten, Ort, Remote | **ja** |
| Keine Zahl über einen Menschen | **ja** — und die Gesamtzahl fehlt bewusst (ADR-0026): sie verriete über die Differenz, wie viele Profile *nicht* freigegeben sind |
| Freigabe je Zeile aus dem Ledger | **ja**, über `/check-batch` (ADR-0030) |
| Keine Sortierung nach Passung | **ja** — es gibt gar keine Passung auf dem Server |
| Häkchenliste je Person | **nein** — die Passung rechnet der Browser, und nur für *Stellen* |
| Belege (GitHub, Lebenslauf) am Treffer | **nein** |
| Gespeicherte Suche | **nein** — jeder Aufruf ist eine neue Frage |
| Entwurf einer Ansprache | **nein** |

**Damit ist offene Entscheidung 2 beantwortet:** `scout-service` löst
`/candidates` ab, denn es ist dieselbe Frage mit einer schwächeren Antwort. Die
Ablösung ist aber kein Umzug — die harten Teile (Ledger je Zeile, keine
Gesamtzahl, Firmenzwang) sind fertig und müssen mitgenommen, nicht neu erfunden
werden.

Nebenbei fällt dabei eine bekannte Lücke: die Kandidatenliste blättert mit einem
Zeiger und hat **keinen Weg auf Seite 2** in der Oberfläche.

### Das „Mandat" ist zu drei Vierteln schon da — verteilt auf zwei Dienste

Der Entwurf beschreibt ein Mandat mit Sichtbarkeit, Konditionen und Regeln zum
jetzigen Arbeitgeber. Gemessen:

| Im Entwurf | heute | wo |
|---|---|---|
| „Wer darf mich finden?" | **Ledger** — `profile.visibility:public`, je Unternehmen | consent-service |
| „Was sehen sie?" (Profil/Belege/Lebenslauf einzeln) | **Ledger** — `profile.*`, `github.visibility:public`, `resume.requested/granted/declined` | consent-service |
| Verfügbarkeit, angestellt ja/nein, Notiz | **Marktstatus** (`MarktstatusV1`) | transfer-service |
| „ansprechbar?" als abgeleitete Aussage | **`is_approachable`** — schon abgeleitet mitgeschickt, damit kein Client die Regel selbst reimt | transfer-service |
| Jetziger Arbeitgeber erfährt nichts | **gebaut** — Dreieckskonsens, E2E-Reise „der Arbeitgeber wird nie gefragt" | transfer-service |
| Eintrittstermin, Gehaltsspanne, Pensum, Reisebereitschaft | **fehlt** | — |
| Ausgeschlossene Unternehmen (namentlich) | **fehlt** | — |

**Daraus folgt eine Warnung für das ADR:** ein `advisor-service` mit eigener
Mandatstabelle würde Sichtbarkeit ein zweites Mal speichern. Das verbietet
ADR-0020 wörtlich — *Sichtbarkeit lebt im Ledger, nirgends sonst* — und
ADR-0013 dazu, weil ein Widerruf sofort wirken muss. Das Mandat ist also **eine
Sicht über Ledger und Marktstatus** plus vier bis fünf echte neue Felder, nicht
ein neuer Speicher.

### Fähigkeiten im Ledger: es sind fünf

Im Quellbaum kommen genau diese vor:

```
profile.visibility:public
github.visibility:public
resume.requested   resume.granted   resume.declined
```

Die dreistufigen Gespräche des Entwurfs (Stufe 1 Profil → Stufe 2 Belege und
Lebenslauf → Stufe 3 Klarname) brauchen Fähigkeiten je Stufe und je Unternehmen.
**Das ist die grösste offene Frage am Berater** und keine Kleinigkeit: jede neue
Fähigkeit ist ein Wort, das die Auskunftsseite, die Löschung und die
Widerrufsseite mitführen müssen.

---

## Was für alle drei Dienste sofort gilt

- **Wer eine Personenzeile hält, ist Löschempfänger.** `LoeschempfaengerTests`
  (in `WorkerTransfer.Ganzes.Tests`) liest das EF-Modell und geht rot, sobald
  ein Dienst eine personenbezogene Tabelle bekommt und nicht in der Kaskade
  steht. Für `scout-service` (Suchen, Treffer-Auskunft), `advisor-service`
  (Mandat, Gespräche) und `assessment-service` (Aufgaben, Lösungen,
  Bewertungen) heisst das: **alle drei sind Empfänger, ab der ersten Tabelle.**
- **Ein Dienst-zu-Dienst-Rumpf ist ein typisierter Vertrag**, nie ein anonymes
  Objekt — sonst wiederholt sich der Benachrichtigungsdraht, der vier Sprünge
  lang nie angekommen ist.
- **Der Entwurf hat kein `assessment-service`-Gegenstück im Bestand.** Es ist
  der einzige der drei, der vollständig neu ist.

---

## Die offenen Entscheidungen, neu sortiert

1. ~~**`/me/scouting`: A, B oder C.**~~ **Entschieden:** keins davon — eine
   **Nachricht** wie bei LinkedIn, kein Protokoll. Steht in
   [ADR-0033](adr/0033-beleg-und-sichtbarkeit.md); damit entsteht die
   sensibelste Tabelle des Systems gar nicht erst.
2. **Wie viele Ledger-Fähigkeiten trägt der Berater?** Drei Stufen × je
   Unternehmen, oder eine Fähigkeit je Stufe mit dem Unternehmen als Umfang.
3. **Wo liegt das Mandat?** Empfehlung: Sichtbarkeit bleibt im Ledger,
   Verfügbarkeit im Marktstatus, und der Berater hält **nur** was heute nirgends
   steht (Termin, Spanne, Pensum, Ausschlüsse).
4. **Lebt ein Suchergebnis?** Der Entwurf schlägt vor: nur die Anfrage
   speichern, nie das Ergebnis, damit ein Widerruf sofort wirkt. Das deckt sich
   mit ADR-0013 und sollte so ins ADR.
5. **Trägt der Berater eine KI?** Wie `/profiles/me/draft` (ADR-0024): entwerfen
   ja, über Dritte sprechen nie, selbstständig senden nie.

---

## Reihenfolge, die daraus folgt

Der Entwurf sagt „vor dem Code steht ein ADR", und er meint eins je Dienst. Die
Messung legt eine Reihenfolge nahe:

1. **ADR: Beleg und Sichtbarkeit** — löst ADR-0022 §„darf wiederkommen" ein,
   entscheidet `/me/scouting` und die drei Herkunftsklassen (genannt / belegt /
   vorgeschlagen). Ohne ihn hängen die anderen beiden in der Luft.
2. **ADR: `scout-service`** — inklusive Ablösung von `/candidates`.
3. **ADR: `advisor-service`** — Mandat als Sicht, Stufen als Fähigkeiten.
4. **ADR: `assessment-service`** — der einzige ohne Bestand, und der mit dem
   grössten Missbrauchspotenzial (unbezahlte Arbeit als Aufgabe getarnt).
