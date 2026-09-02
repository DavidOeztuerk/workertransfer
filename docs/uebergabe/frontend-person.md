# Übergabe — `features/person` (apps/web)

**Territorium:** ausschliesslich `apps/web/src/features/person/**` und diese
Datei. Kein Docker, kein `dotnet` — nur `pnpm`/`npx` in `apps/web`.

**Stand:** 02.09.2026 — **Lesephase begonnen, noch keine Datei angelegt.**
Diese Datei wird nach *jeder* fertigen Seite fortgeschrieben.

---

## 1. Die acht Seiten und die Reihenfolge

| # | Pfad | alte Quelle | Stand |
|---|---|---|---|
| 1 | `/consents` | `routes/consents.tsx`, `consent/client.ts` | offen |
| 2 | `/profile` | `routes/profile.tsx`, `profile/client.ts` | offen |
| 3 | `/resume` | `routes/resume.tsx`, `resume/client.ts` | offen |
| 4 | `/portfolio` | `routes/portfolio.tsx` (+ `portfolio-item.tsx`), `portfolio/client.ts` | offen |
| 5 | `/github` | `routes/github.tsx`, `github/client.ts` | offen |
| 6 | `/my-data` | `routes/my-data.tsx`, `export/…` | offen |
| 7 | `/settings` | `routes/settings.tsx` | offen |
| 8 | `/delete-account` | `routes/**account-deletion**.tsx` (nicht `delete-account.tsx`) | offen |

**Warum diese Reihenfolge.** `/consents` zuerst: dort steht der Freigabeschalter
(`ConsentSwitch`) und dort entsteht der Umgang mit den drei Sätzen „leer" /
„nicht freigegeben" / „der Ledger schweigt" (503). Beides erben die vier
folgenden Seiten. `/delete-account` zuletzt, weil es die heikelste Seite ist und
von der Sitzungsbehandlung aller anderen profitiert.

**Mittendrin:** nichts. `features/person/` ist leer, es gibt nichts
Halbfertiges aufzuräumen.

---

## 2. Aufbau, der gelten soll

```
src/features/person/
  api/{consent,profile,resume,portfolio,github,export,account}.ts
  lib/{useAsync,session}.ts
  pages/{ConsentsPage,ProfilePage,ResumePage,PortfolioPage,PortfolioItemPage,
         GithubPage,MyDataPage,SettingsPage,DeleteAccountPage}.tsx (+ .test.tsx)
  test/render.tsx
  routes.tsx        ->  export const personRoutes
```

---

## 3. Regeln, die hier keine Stilfragen sind

- **Freigabeschalter ist ein Schalter** (`ConsentSwitch`), keine Ankreuzbox —
  eine Einwilligung wirkt sofort, eine Box verspricht „gilt beim Absenden".
- **Drei verschiedene Sätze:** leer · nicht freigegeben · Ledger schweigt (503).
  Bei 503 **weder** Daten **noch** „nichts vorhanden".
- **`/delete-account`:** vor dem Knopf steht, was verschwindet, dass es nicht
  sofort geschieht, und der unangenehme Teil laut. Zwei Schritte, zweiter Knopf
  anders formuliert. Danach „läuft", nie „erledigt", kein Fortschrittsbalken.
  **Der Erfolgszustand hat Vorrang vor der Anmeldeaufforderung.**
- **Passung:** Checkliste, nie Zahl/Prozent/Rangfolge; wer nichts eingetragen
  hat, sieht **kein** „0 von 3".
- **Draht ist snake_case.** Pfade bleiben englisch, Oberfläche bleibt deutsch,
  sichtbare Texte wörtlich aus dem alten Code, kein Farbliteral.

---

## 4. Noch offen

Alles. Nächster Schritt: alte Quellen und die acht `.test.tsx` lesen.
