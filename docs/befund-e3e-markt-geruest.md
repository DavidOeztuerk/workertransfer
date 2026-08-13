# Befund & Soll — E3e: Markt, Gespräche, Übersicht und das Gerüst

`/market`, `/transfers`, `/overview`, `/verify`, `AuthLayout` — und die Schale
`app.tsx`. Der letzte Schnitt von Prompt E.

Gemessen am 13.08.2026.

## Ist-Stand

| Datei | Zeilen | Tests | Besonderheit |
|---|---|---|---|
| `routes/market.tsx` | 290 | 10 | drei rohe Radios + Checkbox |
| `routes/transfers.tsx` | 189 | 13 | — |
| `routes/overview.tsx` | 189 | 9 | zählt Vorgänge, nie Personen |
| `routes/verify.tsx` | 159 | 6 | `AuthLayout`-Familie |
| `routes/auth-layout.tsx` | 53 | — | die Hülle selbst |
| `app.tsx` | 574 | (in `app.test.tsx`) | der letzte rohe `<select>` |

Dazu `/invitation` (127), aus E3d zurückgestellt: sie gehört in die
`AuthLayout`-Familie und wird hier mit umgestellt.

## Der Fund: der vierte Fall — und der folgenschwerste

```ts
export async function getMyMarketStatus(): Promise<MarketStatus> {
  const fallbackStatus: MarketStatus = {
    subject_id: "", availability: "unavailable", employed: false,
    note: "", is_approachable: false, updated_at: "",
  };
  …
  if (!res.ok) return fallbackStatus;
  } catch { return fallbackStatus; }
```

Der Kommentar darüber ist der **beste** der vier Fälle. Er wählt die sichere
Richtung und begründet sie:

> „Die Voreinstellung darf nie zugunsten des Marktes ausfallen. Ein Netzfehler
> ist keine Zustimmung."

Das stimmt, und für die **Anzeige** ist es genau richtig. Die zweite Hälfte fehlt
auch hier — und hier wiegt sie am schwersten, weil dieser Ersatz nicht nur
angezeigt, sondern **in ein Formular geschrieben** wird:

1. `market-service` antwortet nicht.
2. Der Ersatz füllt das Formular: „gerade nicht ansprechbar", **leere Notiz**.
3. `isPending` ist vorbei — die Abfrage *gelang* ja. Kein Fehlerzustand.
4. Wer jetzt irgendetwas anfasst und speichert, schickt `availability:
   "unavailable"` und `note: ""`.

Damit hat die Person ihre **Ansprechbarkeit zurückgezogen und ihre Notiz
gelöscht**, ohne es zu wollen. Auf einem Transfermarkt ist das der teuerste
stille Schreibvorgang, den es gibt: sie verschwindet.

Es ist derselbe Fund wie `isGranted` (E3b), `getNotificationPreferences` (E3c)
und `getOwnCompanyProfile` (E3d) — **viermal dieselbe Form**:

| | erfundener Wert | Begründung im Code | was sie überging |
|---|---|---|---|
| E3b | „nicht freigegeben" | die gefährlichere Lüge wäre „freigegeben" | Schalter blieb bedienbar |
| E3c | „alles abonniert" | ein Netzfehler ist keine Abbestellung | Schalter blieb bedienbar |
| E3d | „noch kein Profil" | (keine) | Formular blieb speicherbar |
| E3e | „nicht ansprechbar" | ein Netzfehler ist keine Zustimmung | Formular blieb speicherbar |

Drei von vier Begründungen sind **richtig** — und alle vier betrachten nur die
Anzeige, nie das Schreiben.

## Zusagen, die kein Refactor anfassen darf

- **Der Marktstatus ist keine Kündigung.** „Hört zu" heißt nicht „geht".
- **Ein Netzfehler ist keine Zustimmung** — der Ersatz fällt nie zugunsten des
  Marktes aus.
- **Ein Transfer entsteht nur aus drei Ja**, und der aktuelle Arbeitgeber wird
  **nie gefragt** — die Plattform weiß nicht einmal, wer er ist.
- **Die Übersicht zählt Vorgänge, nie Personen** (ADR-0022/0026), und sie zeigt
  **nur, was auf eine Entscheidung wartet**. Was von selbst läuft, steht nicht
  da — sonst wäre es eine Liste, und Listen übersieht man.
- **Der Freigabe-Verlauf steht bewusst NICHT in der Übersicht**, sondern nur in
  `/my-data`.
- `/verify`: eine **bestätigte Adresse bei abgelehntem Unternehmen** ist ein
  eigener Zustand, und die Bestätigung scheitert nie an einem vergebenen Namen.
- `AuthLayout` ist eine **Hülle, keine Route** — `/login` und `/register` sind
  zwei Adressen, die sich darin ablösen.

## Entschieden (13.08.2026)

- **`getMyMarketStatus` liefert `MarketStatus | null`.** `null` heißt „nicht
  abrufbar"; die Seite sagt das und zeigt **kein Formular**. Die alte Zusage
  („nie zugunsten des Marktes") gilt damit strenger als vorher: es wird gar
  nichts mehr erfunden, in keine Richtung.
- **Die `AuthLayout`-Familie wird zusammen umgestellt** — `/login`, `/register`,
  `/verify`, `/invitation` und die Hülle. Eine von fünf umzustellen hinterlässt
  zwei Systeme im selben Rahmen; das war schon in E3d der Grund, `/invitation`
  zurückzustellen.
- **Der `CompanySwitcher` in `app.tsx` wird ein `Select`** — der letzte rohe
  `<select>` im Projekt.
- **Der „Stand"-Bereich der Übersicht wird NICHT gebaut.** Er stünde in der
  Routenkarte für E3e, aber er zeigte „wie viele Unternehmen dich gerade sehen" —
  eine Zahl über Blicke auf einen Menschen. Das ist genau die Sorte Zahl, die
  ADR-0022 fernhält, und sie gehört in eine eigene Abwägung, nicht in eine
  Umstellung.
