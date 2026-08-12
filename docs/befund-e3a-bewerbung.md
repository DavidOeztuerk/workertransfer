# Befund & Soll — E3a: Bewerbung

Stand 12.08.2026. Gruppe „Bewerbung" aus
[der Routenkarte](oberflaeche-routenkarte.md): `jobs`, `candidates`,
`applications`, `career` — **1.163 Zeilen**, plus die neue Route für das
Bewerbungsformular.

Vor dem Code der Befund, so abgesprochen: gemessener Ist-Stand, die Zusagen, die
kein Refactor anfassen darf, und die offenen Verhaltensfragen.

## Ist-Stand

| Route | Zeilen | Tests | Endpunkte | `isPending` | `role="alert"` |
|---|---|---|---|---|---|
| `jobs.tsx` | 457 | 23 | `searchJobs`, `getJob`, `apply` + profile/companies | 4 | 3 |
| `candidates.tsx` | 418 | 23 | `listCandidates` + market/resume/github/transfers | 9 | 3 |
| `applications.tsx` | 146 | 6 | `listMyApplications`, `withdraw` | 1 | 2 |
| `career.tsx` | 142 | 5 | `getCompanyBySlug`, `searchJobs` | 3 | 0 |

`candidates.tsx` ist mit **neun** Ladezuständen die dichteste Seite der
Anwendung: Suche, Seitenweiterschaltung, Anfrage je Kandidat, Marktstatus,
GitHub-Belege, Lebenslauf-Anfrage — alles auf einer Route.

## Der CSS-Gewinn ist kleiner, als die Zeilen vermuten lassen

Gemessen: von **27** Klassen dieser Gruppe gehören ihr nur **12** allein.

**Kann umziehen (12):** `candidates`, `candidates__asked`, `candidates__github`,
`candidates__headline`, `candidates__market`, `candidates__meta`,
`candidates__repos`, `candidates__skills`, `jobs__apply`, `jobs__filters`,
`jobs__hiring`, `jobs__skills`.

**Bleibt in `styles.css`, weil andere Routen sie noch benutzen (15):**

| Klasse | weitere Nutzer |
|---|---|
| `auth__alert` | **16** |
| `page`, `page--narrow`, `page__header`, `page__lead` | **15** |
| `page__note` | 9 |
| `requests__meta` | 8 |
| `requests`, `requests__row`, `requests__title`, `requests__actions` | 3 |
| `team`, `team__role` | 2 |
| `market__choice`, `market__hint` | 1 |

Die vier Routen benutzen `.page*` danach nicht mehr (das macht `Page`), aber die
**Regel** bleibt stehen, bis ihr letzter Aufrufer weg ist — dieselbe Regel wie
bei `.auth__alert` in E2. Erwartbarer Gewinn: **grob 60–90 Zeilen**, nicht 300.

## Zusagen, die kein Refactor anfassen darf

Aus den Testnamen gesammelt, nicht aus dem Code geraten. Jede ist ein Test, der
rot werden **muss**, wenn sie fällt.

**Die Passung (ADR-0022), auf `/jobs`:**
- „names what you have and what you lack — **and no percentage anywhere**"
- „marks the missing one as missing, not merely as absent from the list"
- „points at the profile of someone who has none at all, **not just an empty one**"
  — wer nichts eingetragen hat, bekommt **kein „0 von 3"**, sondern den Hinweis
  aufs Profil. Nichts gesagt ist nicht nichts gekonnt.
- „shows no skill line at all when the job names none"
- „asks for the profile **once**, not once per job"

**Der Consent-Ledger, auf `/candidates`:**
- „**does not even ask** without an active company"
- „shows **nothing at all** while the ledger is silent" — Schweigen ist weder
  „nichts freigegeben" noch „kaputt"
- „says an empty list means nobody released — **not that something broke**"
- „**never promises a total** — the count says nothing about who is hidden"
- „asks **separately** from the resume — one grant must not carry the other"
- „does not consult the ledger for a person who was **never asked**"
- „does not invent a reason when the status is gone"

**Die Bewerbung, auf `/jobs`:**
- „does not offer a checkbox for the profile — **it is not a choice**"
- „says how to undo it, **right where it was done**"
- „does not call a silent dependency a rejection"
- „merkt sich die Stelle **UND** wechselt zur Anmeldung"

**Der Widerruf, auf `/applications`:**
- „offers withdrawing only **while something is actually shared**"
- „**still offers it while the company is reading**"
- „says plainly that a withdrawn application **closed the access**"

**Die Karriereseite:**
- „says an unknown address **is unknown**, rather than showing an empty frame"
- „points at **the one place** where applying happens"

## Die neue Route: das Bewerbungsformular

Heute steckt `ApplyBox` in `jobs.tsx:320–420` und klappt **innerhalb** der
Stellenkarte auf; über `?stelle=<uuid>` kommt es vorgeklappt.

**Durchs Gateway ist der Weg belegt** (E2.5): die Dokumentregel
`HeaderRegexp("Sec-Fetch-Dest", "^document$")` hat `priority: 200`, die
API-Ausnahme `PathRegexp("^/jobs/[^/]+/applications")` hat `priority: 100`. Eine
**Seite** unter `/jobs/<uuid>/…` gewinnt also. Trotzdem gilt die Hausregel: mit
**eingetippter Adresse und F5** prüfen, nicht mit einem Klick.

Was die Route braucht: die Stellendaten auf einem kalten Deep-Link (`getJob`),
einen Zustand für „Stelle unbekannt oder zurückgezogen", und den Rückweg
(`Page`-`back`, in E1 dafür gebaut).

## Entschieden (12.08.2026)

1. **Die gemerkte Absicht führt direkt auf die Bewerbungsseite.** Nach dem
   Anmelden geht es auf `/jobs/<id>/apply` statt auf die Liste. Die ~25 Zeilen
   `?stelle=`-Sonderlogik in `jobs.tsx` fallen damit weg — sie holten die
   gemerkte Stelle einzeln und sortierten sie vorn ein, weil sie sonst durch
   Filter und Seitengrenzen fallen konnte. Die Adresse war ohnehin nie für
   Menschen gedacht, sondern unser eigenes Weiterleitungsziel.
2. **Die Passung steht auf beiden Seiten.** Auf der Liste hilft sie beim
   Aussortieren, auf der Bewerbungsseite beim Formulieren — man sieht, welche
   Fähigkeit fehlt, während man das Anschreiben tippt. Bleibt eine Liste mit
   Haken, niemals eine Zahl (ADR-0022), und schweigt weiter für jemanden ohne
   eingetragene Fähigkeiten.
3. **`candidates.tsx` wird erst umgestellt, dann über eine Teilung
   entschieden.** Grund: die 418 Zeilen sind kein Monolith, sondern vier
   Bauteile (`CandidatesRoute`, `ResumeRequestButton`, `MarketAccess`,
   `GitHubEvidence`) plus viel Darstellungscode — genau den nehmen `Page`,
   `RowList`/`Row`, `Loading`, `Alert` und `Empty` ab. Nach der Umstellung steht
   eine **gemessene** Zahl zur Verfügung statt einer Schätzung.

   Gegen eine Detailseite spricht zusätzlich etwas aus der Datei selbst: sie
   vermeidet sorgfältig, den Ledger über Menschen zu befragen, die niemand
   gefragt hat (*„Eine Abfrage für die ganze Seite statt einer je Karte: der
   Ledger sähe sonst bei jedem Seitenaufruf eine Prüfung zu jeder Person"*).
   Eine **teilbare Adresse über eine Person** wäre der Ort, an dem später
   „Übersicht", „Verlauf", „Bewertung" wachsen. Das entsteht nicht als
   Nebenprodukt eines Refactors.

**Reihenfolge:** `applications` und `career` zuerst (klein, klare Zustände), dann
`jobs` samt der neuen Bewerbungsroute, dann `candidates`.

## Noch offen

- **Verlinkt die Karriereseite direkt auf die Bewerbung je Stelle?** Heute zeigt
  sie auf `/jobs` (*„the one place where applying happens"*). Entscheidbar, wenn
  die Bewerbungsroute steht.

