# `RegulatoryRegime` kennt kein nationales Arbeitsrecht

**Gemeldet:** 20.09.2026 · **Noelia:** 6.4.0 · **Status:** offen

## Was fehlt

`Noelia.Abstractions.Compliance.RegulatoryRegime` kennt vier Werte:

```csharp
public enum RegulatoryRegime
{
    Gdpr,
    AiAct,
    Nis2,
    Dora
}
```

Damit lässt sich **§ 87 Abs. 1 Nr. 6 BetrVG** nicht als
`RegulatoryReference` ausdrücken — die Mitbestimmung bei technischen
Einrichtungen, die geeignet sind, Verhalten oder Leistung von Beschäftigten zu
überwachen.

## Warum das zählt

Für eine Bibliothek ist der Zuschnitt vernünftig: DSGVO, KI-VO, NIS-2 und DORA
sind Unionsrecht und gelten für jeden Verbraucher. Nationales Arbeitsrecht gilt
es nicht.

Für **diesen** Verbraucher ist es die Lücke, die am meisten kostet. Der Kunde
ist ein Arbeitgeber, und bevor dort ein System eingeführt wird, das geeignet ist,
Verhalten oder Leistung zu überwachen, redet der Betriebsrat mit. Der
Mitbestimmungsnachweis ist eines von drei Dokumenten, die dieses Repositorium
erzeugt, und § 87 Abs. 1 Nr. 6 ist sein Rückgrat.

## Reproduktion, ohne WorkerTransfer-Code

```csharp
using Noelia.Abstractions.Compliance;

// Das hier übersetzt nicht: es gibt keinen passenden Wert.
var mitbestimmung = new RegulatoryReference(
    RegulatoryRegime.???,
    "§ 87 Abs. 1 Nr. 6",
    "Der Betriebsrat bestimmt mit bei der Einführung und Anwendung technischer "
    + "Einrichtungen, die dazu bestimmt sind, Verhalten oder Leistung der "
    + "Arbeitnehmer zu überwachen.",
    "Ob eine Betriebsvereinbarung nötig ist und was in ihr steht.");
```

## Was hier NICHT getan wurde

**Kein Umweg.** Die Regel dieses Repositoriums lautet: den Code richtig
schreiben, den Test rot lassen, ein Ticket schreiben, weitergehen.

- **Nicht** auf `Gdpr` gelegt. Eine falsche Fundstelle in einem Dokument, das
  ein Betriebsrat liest, ist schlimmer als keine — sie sieht aus wie eine
  geprüfte Angabe.
- **Nicht** ein zweiter, eigener Zitattyp neben `RegulatoryReference`. Zwei
  Fassungen derselben Sache laufen auseinander, und beim ersten Mal merkt es
  niemand.

Stattdessen reist der Artikel im **Text** der betroffenen Befunde mit
(`Rechtsbezuege.Mitbestimmung`). Ein Mensch findet ihn dort; eine Maschine, die
nach Zitaten filtert, findet ihn nicht. Das ist der ehrliche Zwischenstand und
ausdrücklich keine Lösung.

## Was die Lösung wäre

Ein fünfter Wert, etwa `RegulatoryRegime.National`, plus ein Feld, das den
Rechtsraum benennt (`"DE"`). Dann trägt `RegimeName` etwas Sinnvolles, und
`Citation` liest sich als „BetrVG § 87 Abs. 1 Nr. 6".

Die Alternative — Noelia behält seinen Zuschnitt und erlaubt dem Verbraucher
einen eigenen Regelwerksnamen als Zeichenkette — wäre für diesen Fall genauso
gut und für die Bibliothek billiger.

## Wann dieses Ticket grün wird

Wenn `Rechtsbezuege.Mitbestimmung` von einer Konstanten zu einer
`RegulatoryReference` werden kann, ohne dass jemand Code zurücknimmt — und der
Mitbestimmungsnachweis den Artikel dann als Zitat trägt statt als Satz.
