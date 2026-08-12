# Befund & Soll — E3b: die eigenen Daten

Vier Routen, die alle dasselbe tun: **eine Person pflegt etwas über sich und
entscheidet, wer es sieht.** `/profile`, `/resume`, `/portfolio`, `/my-data`.

Gemessen am 12.08.2026, vor der Umstellung.

## Ist-Stand

| Route | Zeilen | Tests | Zustände heute | Freigabe |
|---|---|---|---|---|
| `/profile` | 297 | 15 | anonym, lädt, Fehler, gespeichert | ein Schalter (öffentlich) |
| `/resume` | 290 | 10 | anonym, lädt, Fehler, gespeichert | **kein** Schalter — je Unternehmen, auf Anfrage |
| `/portfolio` | 331 | 10 | anonym, lädt, Fehler, gespeichert | ein Schalter, getrennt vom Profil |
| `/my-data` | 171 | 5 | anonym, sammelt, unvollständig | — (nur Lesen) |

`/resume` ist der Sonderfall und muss es bleiben: ein Profil ist ein Anschlag am
Brett, ein Lebenslauf nennt echte Arbeitgeber mit Daten — genau das, was der
*aktuelle* Arbeitgeber nicht sehen darf. Deshalb hat er keinen öffentlichen
Schalter, sondern eine Anfrage je Unternehmen. Die Umstellung darf daraus keinen
Schalter machen, auch nicht „aus Einheitlichkeit".

## Drei Funde, die keine Umstellung sind

### 1. Der Freigabeschalter behauptet „nicht freigegeben", wenn er es nicht weiß

In `profile.tsx` und `portfolio.tsx` steht derselbe Ausdruck:

```tsx
const released = visibilityQuery.data === true;
```

Damit sind **drei verschiedene Lagen ein und dasselbe Bild** — Schalter aus:

1. die Person hat nichts freigegeben,
2. die Antwort des Ledgers steht noch aus,
3. der Ledger hat gar nicht geantwortet.

Fall 1 ist wahr. Fall 2 und 3 sind eine Behauptung über eine Einwilligung, die
niemand kennt. `profile-service` hält für genau diese Lage einen eigenen Code
vor — `503`, weil ein `404` („nicht sichtbar") ebenso etwas behaupten würde wie
das Anzeigen des Profils. Die Oberfläche wirft diese Unterscheidung wieder weg.

Der Schaden ist nicht theoretisch: wer die Seite bei stummem Ledger öffnet,
sieht „nicht freigegeben" und legt den Schalter um — ein `grant` auf etwas, das
möglicherweise längst gilt.

### 2. `/resume` zeigt „noch niemand hat gefragt", während es noch lädt

```tsx
{requests?.ok && requests.requests.length === 0 ? <p>Bislang hat niemand …</p> : null}
```

Es gibt einen Test dafür, dass ein **stummer** Ledger nichts behauptet — aber
keinen für den Ladezustand, und der ist vom Leerzustand nicht zu unterscheiden.
Dieselbe Klasse Fehler wie in `applications.tsx` (E3a), dort war es die leere
Karte.

### 3. `/profile` hat die letzte handgebaute Checkbox

```tsx
<label className="wt-checkbox">
  <input type="checkbox" … />
  <span>Remote-Arbeit kommt für mich in Frage</span>
</label>
```

Die Klasse gehört dem Designsystem, das Markup nicht: es fehlen
`.wt-checkbox__box` und `.wt-checkbox__label`. Das Kästchen bekommt also **weder
seine Größe noch `accent-color`**, und der Text hat kein `cursor: pointer`.
`checkbox.css` trägt dazu einen Kommentar, der drei Bestandsstellen nennt
(„jobs.tsx 2x, profile.tsx 1x") — die beiden in `jobs.tsx` sind mit E3a gefallen,
diese ist die letzte. Mit ihr fällt auch der Kommentar.

## Zusagen, die kein Refactor anfassen darf

Geerntet aus den 40 Testnamen dieser Gruppe. Jede ist ein Satz, der einmal
begründet wurde:

- **Der Ledger sagt, was gilt — nicht der Wunsch des Klicks.** Nach einem
  abgelehnten Umlegen geht der Schalter zurück; „sichtbar", obwohl nichts
  freigegeben wurde, wäre die gefährlichere Lüge.
- **Freigeben lässt sich nur, was es gibt.** Kein Schalter ohne gespeichertes
  Profil bzw. ohne eine Arbeit.
- **Die Freigabe geht in den Ledger, nie durch das Profil** (ADR-0020: das
  Profil hat kein Sichtbarkeitsfeld).
- **Portfolio und Profil sind zwei Freigaben.** Man kann ansprechbar sein, ohne
  seine Arbeiten zu zeigen.
- **`GRANTED` heißt „wurde einmal gewährt", nicht „gilt jetzt".** Nur `active`
  entscheidet, ob ein Zurückziehen angeboten wird.
- **Abgelehnt bleibt abgelehnt** — dasselbe Unternehmen fragt nicht erneut.
- **Leeres Enddatum heißt „bin noch dort"**, nicht „Lücke". Leerer Link wird
  `null`, nicht `""`; leeres Jahr wird `null`, nicht `0`.
- **Der lokale Dateiname wird nie gezeigt** — er ging nie zum Server.
- **Die KI fragt nichts, bis jemand drückt**, der Hinweis steht am Knopf, der
  Entwurf landet nur im Feld, und über vorhandenem Text warnt der Knopf (ADR-0024).
- **Löschen ist ein Link, kein Nachbarknopf** neben „Herunterladen".
- **`/my-data` enthält den Freigabe-Verlauf**, den die Übersicht bewusst weglässt.

## Entschieden (12.08.2026)

### Abgespaltene Routen: Portfolio ja, Lebenslauf nein

Der Plan hatte `/resume/positions/$id` mit Vorbehalt notiert. Der Vorbehalt
gewinnt:

- **`/portfolio/new` und `/portfolio/$index` werden eigene Routen.** Arbeiten
  sind voneinander unabhängig — man schreibt eine, nicht drei im Vergleich. Eine
  Seite mit fünf aufgeklappten Formularen ist genau der Zustand, den niemand
  überblickt.
- **Die Stationen des Lebenslaufs bleiben EIN Formular** (`/resume`, kein
  `/resume/positions/$id`). Man bearbeitet sie im Vergleich — „von wann bis
  wann" ergibt nur zusammen Sinn, und die **Reihenfolge ist die Aussage**. Eine
  Route je Station würde das Wichtigste unsichtbar machen: die Lücke zwischen
  zwei Stationen.

Der Preis der Portfolio-Aufteilung, ehrlich benannt: `PortfolioItem` hat **keine
ID**, und gespeichert wird das ganze Feld (PUT). Die Adresse kann also nur der
Index sein, und zwei Tabs, die gleichzeitig etwas ändern, adressieren dann
verschiedene Arbeiten. Sichtbar bleibt es, weil die Formularseite den Titel der
Arbeit in die Überschrift nimmt — wer `/portfolio/2` öffnet und dort eine andere
Arbeit liest als erwartet, sieht es sofort. Eine ID einzuführen wäre eine
Vertrags- und Migrationsänderung und gehört nicht in eine Umstellung.

### Der Ladezustand des Ledgers wird sichtbar

`released` bekommt drei Werte statt zwei. Solange die Antwort aussteht, ist der
Schalter **deaktiviert** und sagt das; bleibt der Ledger stumm, sagt die Seite
das ebenfalls und legt nichts um. Das ist dieselbe Regel, nach der
`profile-service` `503` antwortet statt `404`.

### `DraftHelp` zieht aus der Route

66 der 297 Zeilen von `profile.tsx` sind die Formulierungshilfe, und an ihr
hängen vier ADR-0024-Zusagen. Sie zieht nach `src/profile/DraftHelp.tsx` — wie
`CandidateCard` in E3a, aus demselben Grund: die Route soll das Formular zeigen,
nicht drei Gespräche gleichzeitig führen.

## Nicht in diesem Schnitt

- **Ein `FileField`-Primitiv.** `AttachmentField` baut sich ein `wt-field` von
  Hand, weil das Designsystem kein Dateifeld hat. Es gibt genau **einen**
  Aufrufer; ein Primitiv für einen Aufrufer ist eine Vermutung darüber, wie der
  zweite aussieht. Bleibt handgebaut, mit dem Kommentar, warum.
- **Der Upload-Fortschritt.** `uploadAttachment` ist ein `fetch`, kein Stream;
  ein Balken wäre erfunden.


## Ergebnis (13.08.2026)

| Datei | vorher | nachher |
|---|---|---|
| `routes/profile.tsx` | 297 | 248 |
| `profile/DraftHelp.tsx` | – | 69 (neu) |
| `routes/resume.tsx` | 290 | 297 |
| `routes/portfolio.tsx` | 331 | 175 |
| `routes/portfolio-item.tsx` | – | 345 (neu) |
| `routes/my-data.tsx` | 171 | 168 |
| `apps/web/src/styles.css` | 534 | 517 |

Tests: **40 → 59** in dieser Gruppe (Web gesamt 414 → 433, Dateien 46 → 48).

`resume.tsx` ist **länger** geworden — der fehlende Ladezustand und die
Fallunterscheidung in `RequestRow` kosten Zeilen. Das ist der Preis dafür, dass
„es lädt" nicht mehr wie „niemand hat gefragt" aussieht.

### Der Ledger-Fund reichte tiefer als gedacht

Der Befund vermutete das Problem in den Routen. Es lag im **Client**: `isGranted`
gab bei Netzfehler *und* bei Fehlerantwort `false` zurück — die Unterscheidung
war schon weg, bevor eine Route sie hätte treffen können. Der Kommentar dort
nannte die Wahl ausdrücklich und begründete sie richtig („ein Schalter, der
versehentlich ‚freigegeben' behauptet, wäre die gefährlichere Lüge"), schloss
damit aber nur eine Hälfte.

`isGranted` liefert jetzt `boolean | null`. Die Anzeige bleibt bei Nichtwissen
unverändert aus; neu ist, dass der Schalter dann **gesperrt** ist und die Seite
sagt, warum. Vorher war er bedienbar, und der nächste Klick schickte ein `grant`
für eine Einwilligung, deren Stand niemand kannte.

Zwei Dinge fielen dabei auf:

- **`isGranted` hatte keinen einzigen Test.** Jetzt sechs, darunter die 200er
  Antwort mit unlesbarem Körper — auch das ist kein „nein".
- **Ein Test nagelte die alte Form fest** (`profile/client.test.ts`, „treats an
  unreachable ledger as not visible"). Er nannte im Namen die Zusage („never
  claims a release"), prüfte aber den Mechanismus. Die Zusage gilt weiter und
  steht jetzt ausdrücklich als zweite Behauptung darin.

### Eine Falle, in die ich zuerst gelaufen bin

Der erste Test für den gesperrten Schalter war grün, **ohne etwas zu prüfen**:
`getMyProfile` liefert in `beforeEach` `null`, also ist der Schalter schon wegen
„erst ein Profil speichern" gesperrt. Erst mit gespeichertem Profil *und* der
Gegenprobe auf den Hinweistext misst er die Sache. Gegen den ungefixten Code
laufen gelassen — er fällt.

### Tote Regeln

`.resume__position` hat keinen Aufrufer mehr (resume nutzt `Fieldset`, portfolio
ist aufgeteilt) und ist entfernt. Der Kompatibilitäts-Kommentar in
`packages/ui/src/styles/checkbox.css` nannte drei Bestandsstellen mit rohem
`<input>` — alle drei sind umgestellt, und die Notiz sagt das jetzt.
