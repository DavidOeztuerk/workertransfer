---
name: wt-nachweis
description: Use when building or reviewing anything in WorkerTransfer that maps an observation to a regulation — a Pruefung citing an article, a Nachweis document, the /nachweis pages, an ADR about AI duties, or copy that describes any of it — and when running or changing the evidence report (`make evidence`, `make evidence-check`, `/noelia/report.json`, `Evidence__Secret`, the word list scripts, the exemptions). Holds the forbidden words, the four-field citation shape, which duties a program can and cannot establish, and how the report and its gate work (ADR-0044).
---

# Belege, keine Konformität

WorkerTransfer vermittelt Menschen in Arbeit und setzt dabei Modelle ein. Das ist
der Bereich, in dem eine zu große Behauptung teuer wird — rechtlich und beim
ersten Betriebsrat, der nachfragt.

## Die Wortliste

| Nie | Stattdessen |
|---|---|
| zertifiziert, Zertifikat, Zertifizierung *(über unser Ergebnis)* | Nachweis, Beleg, Evidenzbericht, Selbstauskunft |
| bescheinigt hiermit, wir bestätigen | beobachtet, aufgezeichnet, gemessen |
| konform, erfüllt Art. X, DSGVO-konform | Beleg für Art. X, eine Tatsache, nach der Art. X fragt |
| geprüft durch, auditiert | nachgerechnet, ausgelesen |
| KI-gestützte Bewertung, Passung, Score | Häkchen, Beleg, Herkunft |

Nach **Art. 42/43 DSGVO** darf nur eine Aufsichtsbehörde oder eine nach
EN ISO/IEC 17065 akkreditierte Stelle zertifizieren. BSI C5 attestiert nur ein
zugelassener Prüfer, SecNumCloud qualifiziert nur die ANSSI. Wir sind nichts
davon.

„Zertifiziert" ohne Akkreditierung ist in der EU eine irreführende
Geschäftspraxis — RL 2005/29/EG gegenüber Verbrauchern, RL 2006/114/EG zwischen
Unternehmen. Das ist ein echtes Risiko, kein theoretisches.

Vanta, Drata und Secureframe schreiben aus demselben Grund überall, SOC 2 sei
eine *attestation* und keine *certification*. Ihnen hier zu folgen ist der
sichere Weg, nicht der ängstliche.

**Ein Test erzwingt das.** Er scannt jeden Geltungssatz und jeden Vorbehalt.
Erweitere ihn, statt dich auf die Durchsicht zu verlassen.

## Jedes Zitat nennt, was es offenlässt

```csharp
new RegulatoryReference(
    RegulatoryRegime.Gdpr,
    "Kap. V (Art. 44–49)",
    Obligation: "Eine Übermittlung in ein Drittland braucht eine Garantie.",
    Reader:     "Welche Garantie dieses Ziel deckt, und ob dafür eine "
              + "Übermittlungs-Folgenabschätzung vorliegt.");
```

`RegulatoryReference` ist Noelias Typ (`Noelia.Abstractions.Compliance`), und
genau dieses Zitat bringt Noelia schon mit
(`RegulatoryReferences.GdprThirdCountryTransfer`) — das Beispiel zeigt die Form,
nachgebaut wird es nicht. Was Noelia nicht mitbringt, steht in
`src/shared/WorkerTransfer.ServiceDefaults/Checks/AdditionalRegulatoryReferences.cs`.
Bis ADR-0045 hieß der Typ bei uns `Rechtsbezug`, das letzte Feld `Leser`.

`Reader` ist **nie leer**, und zwar als Test. Ein Zitat, das seinen eigenen
Artikel erledigte, wäre genau die Anmaßung, gegen die das Vokabular existiert.

In der Oberfläche steht `Reader` **in derselben Zeile** wie das Zitat, nie als
Fußnote. Eine Fußnote überlebt weder einen Screenshot noch das Einfügen in eine
fremde Tabelle.

## Was ein Programm zeigen kann — und was nicht

**Kann:** dass eine Aufzeichnung existiert, automatisch entsteht, einen Zeitraum
abdeckt und seither unverändert ist · dass ein Host in einer bestimmten
Jurisdiktion liegt · dass ein Feld existiert oder nicht existiert · dass ein
Widerruf beim nächsten Zugriff wirkt.

**Kann nicht:** ob ein AV-Vertrag angemessen ist · ob eine Risikobewertung etwas
taugt · ob im Prompt personenbezogene Daten stehen · ob eine Nutzung nach
Anhang III hochriskant ist · ob menschliche Aufsicht wirksam ist.

Wenn eine vorgeschlagene Prüfung die zweite Spalte bräuchte, ist sie keine
Prüfung. Sie ist eine Frage an einen Menschen und gehört in `Reader` oder in
einen Vorbehalt.

## Die Fristen, und warum sie dastehen müssen

Die KI-VO läuft gestaffelt an. Stand heute:

| Pflicht | Seit / ab |
|---|---|
| Verbotene Praktiken, KI-Kompetenz | **02.02.2025** |
| GPAI-Pflichten | **02.08.2025** |
| **Art. 50 Transparenz** gegenüber der Person | **02.08.2026 — gilt** |
| Art. 12 Aufzeichnung, Art. 26 Betreiberpflichten | **02.12.2027** |
| Anhang I eingebettete Hochrisikosysteme | 02.08.2028 |

Ein Dokument, das eine Pflicht von 2027 so darstellt, als binde sie heute, lädt
den Leser ein, zu früh Geld auszugeben. **Nenne das Datum.**

## Die Einstufung gehört nicht uns

Anhang III Nr. 4 Buchst. a nennt KI-Systeme für *„die Einstellung oder Auswahl
natürlicher Personen"*. `scout-service` sucht Menschen, `assessment-service`
bewertet eine Arbeitsprobe. Ob das darunter fällt, ist eine Beurteilung der
**Nutzung** und gehört einem Menschen mit juristischer Ausbildung.

ADR-0022 (keine Zahl über einen Menschen) ist erkennbar der Versuch, gar nicht
erst hineinzugeraten. Ob er trägt, entscheidet dieser Mensch.

**Schreibe nie, dass WorkerTransfer nicht hochriskant ist.** Schreibe, welche
Belege vorliegen und wer die Frage beantwortet.

## Was nie in einen Nachweis gerät

- **Kein Wert.** Keine Schlüssel, Token, Verbindungszeichenfolgen, kein roher
  Ausnahmetext. Anbieter schreiben Endpunkte in Ausnahmen.
- **Kein Name einer Person**, und keine Aussage über eine einzelne Person.
  Das KI-Verzeichnis nennt Anbieter und **zählt** — „drei Menschen auf
  `api.anthropic.com`". Nie, wer. ADR-0026 sagt dasselbe für Ereignisse.
- **Keine Zahl über einen Menschen**, auch nicht als Nebenprodukt. ADR-0022 gilt
  auch für das Werkzeug, das ADR-0022 prüft.

## Souveränität ist kein Regelwerk

Keine Verordnung verlangt digitale Souveränität. Sie ist eine Entscheidung des
Betreibers. Wo davon die Rede ist, misst der Nachweis diese Entscheidung — nicht
die Übereinstimmung mit einer Regel. `RegulatoryRegime` bekommt keinen Eintrag dafür.

---

*Die folgenden Abschnitte sind am 30.10.2026 wörtlich aus `CLAUDE.md` hierher
verschoben worden; die verbindliche Zeile („Belege, keine Konformität") steht dort
weiter.*

- `docs/adr/0044-nachweis-belege-statt-konformitaet.md` — der Nachweis: was ein Programm zeigen kann, was nicht, und wer die Anhang-III-Frage beantwortet. 20.09.2026.

## Nachweis und KI-Pflichten

`docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md` ist offen: die Prinzipien dieses
Repositoriums stehen in ADRs und in einer Momentaufnahme vom 02.09.2026, aber
keines davon läuft. Der Auftrag macht sie zu Prüfungen, die rot werden können,
zu einem Bericht, den eine Maschine liest, und zu drei datierten Dokumenten für
drei verschiedene Leser.

Die Linie, in einem Satz: **Belege, keine Konformität.** Ein Programm zeigt
Vorhandensein, Zeitpunkt und Unversehrtheit — nie Angemessenheit. Die Fertigkeit
`wt-nachweis` hält die Wortliste und die Grenze; sie gilt für jeden Satz, der in
ein Dokument oder auf eine Seite gerät.

```
/nachweis alles       # den ganzen Auftrag, Phase für Phase, mit Toren dazwischen
/nachweis 1           # nur eine Phase
```

**Ausgeführt am 20.09.2026 (PR #87).** Seine Phasen 3–5 gehen im Noelia-Umstieg
auf: `ISecurityCheck`, `Noelia.Dashboard` und `RegulatoryReference` liefern, was
`WorkerTransfer.Nachweis` selbst gebaut hat. Sechs Prüfungen bleiben, weil sie
WorkerTransfers eigene sind — fünf in `ServiceDefaults/Checks`, dazu `LedgerCheck`
in consent-service (gezählt am 25.09.2026).

### The evidence report: Belege, keine Konformität (ADR-0044)

Every service answers about **itself** under `/noelia/…` — Noelia's dashboard
since ADR-0045, which replaced the seven hand-built `/nachweis/…` addresses of
20.09.2026 — and `/noelia/report.json` is what the scripts read. `make evidence`
collects all fourteen against the running stack and writes three separate
signed documents: **Datenschutz**, **KI**, **Mitbestimmung**.
`make evidence-check` is the gate and goes red on an unkept promise. Measured 20.09.2026: **83 findings across 14
services**, none unkept.

**Why it exists at all: the principles were written down and not executable.**
`Adr0022Tests` and `DraftBoundaryTests` check the *tree* — but the AI access is
`AiAccessV1`, which means provider, `base_url`, model and key come **per person
at runtime** from the account settings. No test in the world sees where this
tree actually speaks tonight. `docs/KI-EINSATZ-PRUEFUNG.md` is a snapshot from
02.09.2026 and says so; it counts *two* AI consumers where there are now **four**
(profile, jobs, scout, applications).

**The line, and it is a test and not a review.** Nothing says "konform",
"zertifiziert" or "erfüllt Art. X". Under Art. 42/43 DSGVO only a supervisory
authority or a body accredited to EN ISO/IEC 17065 may certify; "certified"
without accreditation is a misleading commercial practice in the EU
(RL 2005/29/EG, RL 2006/114/EG). `DocumentWordTests` reads the scope sentences
out of the generator (`scripts/evidence_documents.py`) and runs without a stack;
`scripts/evidence_words.py` checks what actually came out. Both carry the
English twins (`compliant`, `conform`, `audited`, …) beside the German words. **The disclaimer is exempt from the word list on
purpose** — it *denies* those words ("Es ist KEIN Zertifikat"), and a search that
reads it flags the one sentence that makes the document honest. Measured on the
first run; its wording is pinned from the other side instead.

**Every citation carries `Reader` — what a human still decides — and it is never
empty, as a test.** (`Leser` in our own citation type until ADR-0045; today it is
a field of Noelia's `RegulatoryReference`.) A citation that settled its own
article would be exactly the presumption the vocabulary exists against. On the
page `Reader` stands in the
**same row** as the citation, never as a footnote: a footnote survives neither a
screenshot nor a paste into somebody else's table.

**`Warning` is not a defect, and that distinction is the whole gate.** That
model calls are not recorded is ADR-0024 and a decision; a gate that goes red on
deliberate restraint gets switched off by the next person. Red is only `Fail`.
And a check whose subject is absent reports `NotApplicable`, never `Pass` —
fourteen services without an AI seam would otherwise be fourteen green ticks
over a seam that does not exist.

**The AI directory counts people and names none.** It reports provider label and
host and *counts* — "three people on `api.anthropic.com`". `EfProviderSource`
groups in SQL and does not even fetch the encrypted key: what is not fetched
cannot end up in a finding. ADR-0026 says the same for events.

**The door is its own piece of paper.** `Evidence__Secret`, deliberately
neither the notification secret nor the erasure one: "may trigger a mail", "may
delete everything about a person" and "may see which providers this instance
uses" are three different rights. **Empty means shut**, and shut answers
**404**, not 403 — an operations surface whose existence you can guess is itself
a disclosure. A path below that names no section is 404 too.

**Page and `bericht.json` come from ONE reading.** Two query paths to one
statement drift apart, and the first time nobody notices — and which of the two
went into the signed document nobody can say afterwards.

**The word list matches per syllable, at the syllable start — and that was
measured, not chosen.** A substring search over this tree reported `Capability`
(the ledger's core type) for `ability`, `Benefits` for `fit` and `Availability`
for `ability`: three false alarms over perfectly correct code. Exactly the bug
Girder 4.3.0 had in its masking. What the syllable rule does *not* find is
pinned as its own test: a German compound carrying the word at the **end**
(`Trefferanzahl`) escapes it. **The name is the signal, not the rule** — the
closed field sets (`wt.ki.naht`) stand beside it for that reason.

`bewert` is deliberately **not** in the platform vocabulary: assessment-service
holds exactly one evaluation per case, as text, and the person always reads it
(ADR-0042). Forbidding the word platform-wide would mean excusing thirty
correctly named things, and after the third excuse nobody reads the list.
scout-service still forbids it locally, where a judgement beside a search hit
*would* be the forbidden number.

**Thirteen exemptions exist in the whole tree**, in three services, for three
homonyms: `Note` (English: remark), `Quote` (English: citation) and
`WorkloadPercent` (the workload a person states about the job they want;
`PensumProzent` before the translation). Each carries its reason, and **the reason appears in the finding** —
whoever adds an exemption writes it into a document a works council reads. A
silent list in a test project grows; one that gets read does not. The count is
pinned so a fourteenth stands out.

**`Seitenanfrage.Anzahl` in profile-service became `Seitenlaenge`, and is
`PageRequest.PageLength` today**, exactly like scout-service and for the same
reason. The vocabulary forced the naming, and
that is its job.

**The gate needs the stack, and says so instead of hiding it.**
`make evidence-check` runs inside `make validate` whenever the stack answers;
when it does not, the report names it — the same treatment `validate.sh` gives
the E2E journeys. A tick over a run that never happened is the kind of green
that script was built against.
