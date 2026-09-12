# ADR-0037: advisor-service — Mandat als Sicht, nicht als zweite Wahrheit

**Status:** angenommen (06.09.2026) · **gebaut am 11.09.2026**
**Betrifft:** advisor-service, transfer-service, consent-service, profile-service, resume-service, github-service, notification-service, `web/`
**Verwandt:** ADR-0033 (Beleg und Sichtbarkeit), ADR-0020 (Sichtbarkeit im Ledger), ADR-0013 (Einwilligung wirkt sofort), ADR-0036 (scout-service), ADR-0024 (die KI-Naht), ADR-0027 (Löschung)

**Gebaut am 11.09.2026.** Was dabei über dieses Dokument hinaus entschieden
werden musste, steht in CLAUDE.md unter „advisor-service: the mandate is a
view, not a second store" — vor allem drei Dinge:

1. **Welche Fähigkeit eine Stufe TRÄGT und welche eine Freigabe SCHREIBT, ist
   nicht dasselbe.** Stufe 1 steht an `profile.visibility:*`, schreibt aber
   auch `market.visibility:tenant:<id>` — sie verspricht „Verfügbarkeit", und
   die Verfügbarkeit *ist* der Marktstatus.
2. **`github.visibility:public` wird nie geschrieben.** Sie ist plattformweit
   und kennt keine Firmenfassung; sie in einer Stufenfreigabe mitzuschreiben
   machte aus einer Freigabe an *ein* Unternehmen eine an alle. Belege reisen
   zu Stufe 2 mit, wenn sie ohnehin öffentlich stehen.
3. **Die Gehaltsspanne steht in Stufe 2, nicht in Stufe 3.** §2 dieses Dokuments
   sagte „Klarname plus Mandatsfelder"; gebaut ist die feinere Einteilung —
   Eintritt und Pensum ab Stufe 1, Spanne ab Stufe 2, Klarname und Kontakt ab
   Stufe 3. Ein Eintrittstermin ist der Anlass eines Gesprächs, kein Ergebnis.

## Der Fund, der dieses ADR ausgelöst hat

Die Bestandsaufnahme hat gemessen: das Mandat des Entwurfs ist zu drei
Vierteln schon da, verteilt auf Ledger und Marktstatus. Eine eigene
Mandatstabelle für Sichtbarkeit würde ADR-0020 wörtlich verletzen.

Was fehlt, sind vier Felder, die heute nirgends stehen: Eintrittstermin,
Gehaltsspanne, Pensum, ausgeschlossene Unternehmen.

## Entscheidung 1: das Mandat ist eine Sicht plus vier Felder

advisor-service hält **nur**:

| Feld | Bedeutung |
|---|---|
| `eintrittstermin` | Monat, freiwillig |
| `gehalt_min` / `gehalt_max` | Euro im Monat, freiwillig |
| `pensum_prozent` | 10–100, freiwillig |
| `ausgeschlossene_unternehmen` | Domains, freiwillig |

Sichtbarkeit kommt aus dem Ledger. Verfügbarkeit kommt aus dem Marktstatus
(`is_approachable` bleibt dort abgeleitet). Das EF-Modell trägt **keine**
Spalte, deren Name `sichtbar`, `visible`, `public` oder `freigabe` enthält —
ein Test pinnt das.

## Entscheidung 2: keine `advisor.stageN`-Fähigkeiten

Die größte offene Frage der Bestandsaufnahme: drei neue Ledger-Fähigkeiten je
Unternehmen, oder die bestehenden Sichtbarkeiten?

**Die bestehenden.** Eine zweite Fähigkeit für dasselbe („Profil sichtbar für
Firma X") wäre eine zweite Wahrheit, und die weicht beim ersten Widerruf ab.

- Stufe 1 ⇔ `profile.visibility:tenant:<id>` (und Marktstatus, der schon
  mitreist).
- Stufe 2 ⇔ die bestehenden GitHub- und Lebenslauf-Freigaben für dieses
  Unternehmen. `advance` **schreibt Ledger-Ereignisse**; jedes Lesen fragt
  den Ledger, nicht eine Stufenspalte.
- Stufe 3 ⇔ eine neue Fähigkeit für Klarname plus Mandatsfelder, und der
  Token enthält **keine Ziffer** (`Capability` lässt im Namensraum keine zu):
  `advisor.identity:tenant:<uuid>`.

`advisor.stage1` würde der Parser ablehnen. Das ist kein Zufall der Regex,
sondern der Grund, hier nicht zu nummerieren.

Nach einem Widerruf bleibt „einmal erteilt" stehen, und das Lesen kommt leer
zurück — dieselbe Unterscheidung wie beim Lebenslauf: die Anfrage ist nicht
die Erlaubnis.

## Entscheidung 3: der jetzige Arbeitgeber erfährt nichts

Vorgabe ist die konservativste: unsichtbar für den eigenen Arbeitgeber, und
die Plattform fragt ihn nicht, weil sie nicht weiß, wer er ist. Das ist schon
der Dreieckskonsens im transfer-service. Der Berater erfindet das nicht neu
und legt auch keine Arbeitgeber-Spalte an.

## Entscheidung 4: Gespräche gehören hier, bis etwas vereinbart ist

Ein Gespräch ist ein advisor-Aggregat, solange es um Sichtbarkeitsstufen und
Mandat geht. Erst die Einigung wird ein Vorgang in transfer-service. Ein
`hasStage` am Transfer wäre die Stufe als zweite Tür neben dem Ledger.

Verborgen und nicht vorhanden bleiben 404, byte-identisch — Lebenslauf,
Belege, Mandatsfelder.

## Entscheidung 5: KI erst, wenn jemand Nachrichten tippt

Offen, bis feststeht, dass die Person im Gespräch selbst schreibt. Wenn ja:
Spiegel von ADR-0024 — Entwurf auf Knopf, nie über Dritte, nie selbst senden,
Zusammenfassungen verlassen die Sitzung nicht.

## Was dieses ADR nicht entscheidet

- Scout (ADR-0036).
- Assessment.
- Ob „alle Unternehmen außer diesen" eine Verneinung im Ledger braucht. Solange
  der Ledger kein Deny kennt, gibt es den Modus „alle" nicht.

## Löschung

Ab der ersten Tabelle ist advisor-service Löschempfänger. `"advisor"` gehört
in `Loeschempfaenger.Fremde` und in `LoeschempfaengerTests.Dienste`.
