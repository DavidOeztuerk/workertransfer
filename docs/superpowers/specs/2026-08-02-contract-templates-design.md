# Vertragsvorlagen — Entwurf für später (NICHT gebaut)

Date: 2026-08-02
Status: **Vorgemerkt.** Nicht implementiert. Die rechtliche Prüfung steht aus
und ist ausdrücklich verschoben — dieses Dokument trifft **keine** rechtliche
Aussage, es hält den fachlichen Entwurf fest und sammelt die Fragen, die vor
dem Bauen beantwortet sein müssen.
Related: [Transfer-Vorgang (5.2)](2026-08-02-transfer-deals-design.md), [Ablage (ADR-0021)](../../adr/0021-storage-slim.md), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 5 + Phase 8

## Die Festlegung

> Die Plattform stellt **Vorlagen** bereit. Sie füllt sie aus. Danach geht der
> Entwurf **in die Prüfung**. Erst wenn dort alles stimmt, steht er zur
> Unterschrift bereit.

Vier Stationen, und die dritte ist die, um die es geht.

**Die Plattform erzeugt keinen Vertrag.** Sie erzeugt einen *ausgefüllten
Entwurf aus einer Vorlage, die jemand anders verantwortet.* Der Unterschied ist
nicht sprachlich: ein erzeugter Vertrag erweckt Vertrauen, das die Plattform
nicht einlösen kann, und die Klauseln, die dafür nötig wären — Probezeit,
Kündigungsfristen, Wettbewerbsverbot, Vergütungsbestandteile — kennt das System
nicht und darf sie nicht erfinden.

**Nichts überspringt die Prüfung.** Kein „Standardfall", kein „nur eine
Kleinigkeit geändert", kein automatisches Durchwinken bei unveränderter Vorlage.
Der Moment, in dem die erste Ausnahme eingebaut wird, ist der Moment, in dem die
Prüfung aufhört, eine Zusage zu sein.

## Der Weg

```
Vorlage            (gehört einem Unternehmen, versioniert, unveränderlich)
   │  ausfüllen
   ▼
ENTWURF            veränderbar, sieht niemand außer dem Unternehmen
   │  zur Prüfung geben
   ▼
IN PRÜFUNG         eingefroren — wer ändern will, muss zurück
   │  freigeben          │  beanstanden
   ▼                     ▼
FREIGEGEBEN          ENTWURF (mit Anmerkungen)
   │  unterschreiben
   ▼
UNTERSCHRIEBEN     unveränderlich, für beide Seiten abrufbar
```

**Eingefroren ab „in Prüfung".** Ein Entwurf, der sich während der Prüfung
ändern lässt, macht die Prüfung wertlos — geprüft wurde dann etwas anderes als
das, was unterschrieben wird. Wer ändern will, geht zurück auf `ENTWURF`, und
die Freigabe ist weg. Auch für Tippfehler. Besonders für Tippfehler: der
Unterschied zwischen „30 Tage" und „3 Tage" ist ein Tippfehler.

**Jeder Entwurf trägt, aus welcher Vorlagenversion er stammt.** Ohne das lässt
sich nachher nicht rekonstruieren, was jemand unterschrieben hat — und genau das
ist die Frage, die im Streitfall gestellt wird.

## Wer prüft

Das ist die offene Kernfrage des Entwurfs, und sie ist keine technische.

| Modell | Was es bedeutet |
|---|---|
| **A — das Unternehmen prüft selbst** | Eine benannte Person mit einer Rolle „darf freigeben". Die Plattform stellt nur sicher, dass es passiert ist und wer es war. Sie verspricht nichts über die Qualität. |
| **B — die Plattform prüft** | Wir sagen zu, dass ein Vertrag in Ordnung ist. Das ist eine Zusage über fremde Rechtsverhältnisse und braucht Menschen mit Qualifikation, Haftung und Versicherung. |
| **C — beide Seiten geben frei** | Person und Unternehmen bestätigen den Entwurf, bevor unterschrieben wird. Sagt nichts über Richtigkeit, aber viel über Einvernehmen. |

**Empfehlung: A, ergänzt um C.** Das Unternehmen verantwortet den Inhalt (es ist
sein Vertrag und seine Vorlage), die Person bestätigt, dass sie den Entwurf
gesehen hat und einverstanden ist. Die Plattform protokolliert beides und
behauptet über den Inhalt nichts.

B ist nicht „später auch noch möglich", sondern ein anderes Geschäft. Wer es
will, gründet dafür etwas — es hängt nicht an einer Statusspalte.

## Was die Plattform ausfüllen darf

Nur, was sie **schon weiß und was beide Seiten schon vereinbart haben**:

| Feld | Woher |
|---|---|
| Name der Person | identity-service |
| Unternehmen | identity-service / companies-service |
| Startmonat | Transfer-Vorgang (5.2) |
| Ablöse | Transfer-Vorgang (5.2) |
| Rolle / Beschreibung | `offer_note` aus dem Angebot |

**Und sonst nichts.** Vergütung, Arbeitszeit, Urlaub, Probezeit, Fristen: die
Plattform kennt sie nicht. Sie bleiben Platzhalter, die ein Mensch füllt. Eine
Vorbelegung mit „üblichen Werten" wäre der gefährlichste Bequemlichkeitsgewinn
im ganzen System — sie sieht aus wie eine Empfehlung und wird als eine gelesen.

Ein Platzhalter, der beim Übergang in die Prüfung noch leer ist, blockiert den
Übergang. Ein Vertrag mit `{{gehalt}}` darin darf nicht existieren.

## Wo die Dokumente liegen

`worker-storage` kann das (ADR-0021) — aber ein Vertrag ist nicht wie ein
Portfolio-Anhang.

**Ein unterschriebener Vertrag ist ein beiderseitiger Vorgang, kein
freigegebenes Datum.** Dieselbe Überlegung wie bei der Bewerbung (4.2): dass
zwei Parteien einen Vertrag geschlossen haben, gehört zur Geschichte beider —
ein Widerruf im Consent-Ledger löscht ihn nicht. Er ist auch nicht
consent-geschützt: er entstand aus einer Unterschrift, nicht aus einer
Sichtbarkeitsfreigabe.

Was daraus folgt und **vor** dem Bauen entschieden sein muss: Aufbewahrungsdauer,
wer löschen darf, was bei einem Löschverlangen nach Art. 17 DSGVO passiert (und
welche Aufbewahrungspflichten dagegen stehen), und ob die Plattform überhaupt
Kopien hält oder nur Nachweise darüber, dass unterschrieben wurde.

## Unterschrift

**Bewusst ohne Aussage in diesem Dokument.** Zu klären ist unter anderem, welche
Form ein Arbeitsvertrag im jeweiligen Land verlangt und welche elektronische
Signaturstufe (einfach / fortgeschritten / qualifiziert) dafür genügt — und für
befristete Verträge und Nachweise gelten in Deutschland eigene Formvorschriften,
die eine elektronische Unterschrift nicht in jedem Fall ersetzt.

Das ist kein Detail am Rand: **es entscheidet, ob das Feature das kann, was sein
Name verspricht.** Wenn am Ende „bitte ausdrucken und unterschreiben"
herauskommt, ist das ein legitimes Ergebnis — aber es muss vor dem Bauen
feststehen, nicht danach.

Bis dahin gilt: die Plattform behauptet nirgends, eine Unterschrift sei
rechtsgültig.

## Die Fragen, die vor dem Bauen beantwortet sein müssen

Zu klären, nicht hier zu entscheiden:

1. **Dürfen wir Vorlagen ausfüllen?** Wo verläuft die Grenze zwischen einem
   Formular und einer Rechtsdienstleistung — und ändert sich das, wenn die
   Plattform Felder vorbelegt oder Vorlagen empfiehlt?
2. **Welche Formvorschriften gelten** für Arbeitsverträge, Aufhebungsverträge
   und Transfervereinbarungen in den Ländern, in denen wir laufen? Welche
   Signaturstufe genügt jeweils?
3. **Wer haftet**, wenn eine Vorlage fehlerhaft ist — der Anbieter der Vorlage,
   das Unternehmen, die Plattform? Was ändert daran die Prüfstation?
4. **Wer ist Verantwortlicher** im Sinne der DSGVO für ein gespeichertes
   Vertragsdokument, und wie lange darf es liegen?
5. **Was passiert bei einem Löschverlangen**, wenn Aufbewahrungspflichten
   dagegenstehen?
6. **Dürfen wir Vorlagen mitliefern** oder nur solche verwalten, die ein
   Unternehmen selbst einbringt?
7. Braucht die Rolle „darf freigeben" eine nachgewiesene Qualifikation, oder
   genügt eine benannte Person?

Frage 2 zuerst. Sie kann den ganzen Schnitt umwerfen, und alles andere hängt
daran.

## Was das für heute heißt

**Nichts davon wird jetzt gebaut.** `worker-templates` bleibt leer, es gibt
keinen Endpunkt, keine Statusspalte und keine Vorlage im Repository.

Der Grund steht oben: der erste Baustein wäre die Vorlagenverwaltung, und deren
Zuschnitt hängt an Frage 1 und 6. Etwas zu bauen, das danach anders aussehen
muss, ist teurer als zu warten — und ein halb gebauter Vertragsweg im Code lädt
dazu ein, ihn „mal eben" zu benutzen.

## Selbstprüfung

*Ist eine Prüfstation nicht nur Bürokratie?* Sie ist der einzige Punkt, an dem
ein Mensch das Dokument sieht, bevor es bindend wird. Ohne sie ist die Plattform
ein Automat, der Verträge ausspuckt — und die Frage „wer hat das eigentlich
angesehen?" hat dann keine Antwort.

*Warum friert der Entwurf beim Übergang in die Prüfung ein und nicht erst bei
der Freigabe?* Weil sonst zwischen Prüfung und Freigabe ein Fenster bleibt, in
dem sich der Inhalt ändern lässt. Ein Fenster, in dem niemand hinsieht, ist
genau die Lücke, die man sucht, wenn man etwas hineinschreiben will.

*Warum darf die Plattform die Ablöse einsetzen, aber nicht das Gehalt?* Weil sie
die Ablöse **kennt**: beide Seiten haben sie im Transfer-Vorgang vereinbart. Das
Gehalt hat ihr nie jemand gesagt. Der Unterschied ist nicht die Sensibilität,
sondern ob es eine Tatsache im System gibt oder eine Erfindung wäre.
