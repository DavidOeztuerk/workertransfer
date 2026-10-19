# ADR-0049 — Der KI-Berater ist ein Agent der Person

**Datum:** 08.10.2026 · **Status:** angenommen
**Ersetzt:** die Rolle aus ADR-0037 („Berater = Mandat als Sicht“) — das
Mandat selbst bleibt · **Berührt:** ADR-0024 (KI-Naht), ADR-0047 (Nachrichten),
`docs/VISION-FRAGEN.md` B3, B4, B5, D2, D4, E2, H2

## Zusammenhang

„Berater“ hieß bis heute: ein Mandat mit vier Werten und drei Freigabestufen,
die die Person selbst klickt. Eine Schnittstelle zwischen Person und Firma,
keine Interessenvertretung der Person (B3). Der Katalog hat entschieden: Es gibt
**einen KI-Berater-Agenten, der nur für die Person arbeitet** (B3-A, B4-A, D4),
mit drei Aufgaben — Gehalt, Vertrag erklären, Wechsel begleiten (B5) — und alles,
was er nach außen gibt, gibt die Person frei (D2).

## Entscheidung

**Der Berater spricht zur Person, nicht für sie — in dieser ersten Fassung.**
Er schaut auf ein Gespräch (Bewerbung, Ansprache, Initiativbewerbung) und sagt
der Person, was er sieht: wo das Gespräch steht, was offen ist, was gegen ihre
eigenen Angaben spricht, was sie als Nächstes tun könnte. Nach außen schreibt
er über den bestehenden Weg: „Antwort entwerfen“ legt einen Entwurf ab, den sie
freigibt (ADR-0047). Verhandeln im Namen der Person bleibt ein späterer Schritt
— mit genau diesem Freigabeweg.

**Was er liest — und nur das:**

1. die **gesendeten** Nachrichten dieses einen Gesprächs (nie Entwürfe, nie ein
   anderes Gespräch), den Namen nur als Platzhalter;
2. ihre **eigenen** Angaben: das Mandat (Eintrittsmonat, Gehaltswunsch,
   Pensum) und den frühesten Wechsel aus ihrer Kündigungsfrist (H2) — geholt
   mit **ihrem** Token, also genau, was sie selbst sehen darf.

Kein Profil, kein Lebenslauf, keine Bewertung der Firma aus fremden Quellen,
keine Gehaltszahl, die nicht in diesen Quellen steht.

**Was er nicht tut, als Regel im Prompt und als Grenze im Code:**

- **Kein Rechtsrat.** Klauseln werden erklärt und mit „Lass das prüfen“
  markiert, nie beurteilt (§ 2 RDG, B5). Jede Antwort endet mit dem Hinweis
  auf die Checklisten.
- **Keine Zahl über die Person** und kein „du bist X wert“ (ADR-0022).
  Gehaltsvergleiche verweisen auf `/salaries`, wo jede Zahl ihre Quelle hat.
- **Jede Angabe nennt ihre Quelle** (Gespräch / Mandat / Kündigungsfrist).
- **Für die Firma gibt es ihn nicht.** Die Route antwortet einer Firma 403 —
  ein Agent für beide Seiten wäre der Interessenkonflikt aus D4.

**Aktiviert wird er durch den Klick**, wie jede Entwurfshilfe (ADR-0024): kein
Mitlesen im Hintergrund, kein Lauf beim Eintreffen einer Nachricht. Gespeichert
wird nichts — die Einschätzung geht an den Browser der Person und ist weg, wenn
sie die Seite verlässt.

**Welches Modell:** ihr eigener KI-Zugang aus den Kontoeinstellungen, sonst der
Plattform-Standard (Mistral, EU) — derselbe Weg wie beim Antwortentwurf.

**Das Mandat bleibt** als ihre eigenen, gespeicherten Wünsche; die drei
Freigabestufen aus ADR-0037 bleiben, bis Phase 4 klärt, wie der alte Arbeitgeber
vorkommt.

## Was es kostet

- messages-service fragt zwei Dienste mehr, beide mit dem Token der Person:
  advisor-service (`GET /advisor/me/mandate`) und transfer-service
  (`GET /market/me/notice`). Schweigt einer, berät der Berater ohne diese
  Angabe und **sagt es** — ein stilles Weglassen wäre die Unvollständigkeit,
  die ADR-0022 §3 verbietet.
- Was eine Person in Nachrichten über Dritte schreibt, erreicht das Modell,
  wenn sie den Berater fragt (wie beim Antwortentwurf, ADR-0047).
