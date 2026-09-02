# `GET /jobs` verlangt Anmeldung — die Karriereseite soll aber öffentlich sein

- **Gefunden beim:** Nachziehen der E2E-Reisen nach dem Frontend-Umbau
- **Art:** Widerspruch zwischen zwei dokumentierten Entscheidungen
- **Blockiert:** zwei E2E-Reisen. Im Betrieb: die öffentliche Karriereseite
  zeigt keine Stellen.
- **Nicht entschieden.** Das ist eine Produktfrage, keine Codefrage.

## Was gemessen ist

```
GET /jobs                                  ohne Token -> 401
GET /jobs?company=<id>                     ohne Token -> 401
GET /companies/by-slug/<kuerzel>           ohne Token -> 200/404
```

Im Quelltext ausdrücklich so gewollt
(`src/jobs-service/…/StellenEndpoints.cs:124`):

```csharp
stellen.MapGet("/", async (…) =>
{
    if (akteur.Current is null)
    {
        await NichtAngemeldet(context);
        return;
    }
```

## Die beiden Aussagen, die sich widersprechen

**Für „hinter der Anmeldung":**

- `docs/routenkarte.yml:250` — `GET /jobs`, ohne Token **401**
- `CLAUDE.md:156` — der CI-Job beweist die Weiterleitung genau damit: *„`GET
  /jobs` (401 … — a job list sits behind login)"*
- `CLAUDE.md:289` — *„It answers 401 — a job list sits behind login"*, und
  `scripts/k8s-up.sh` wurde deswegen korrigiert

**Für „öffentlich":**

- `docs/routenkarte.yml:281` — *„weil die Karriereseite oeffentlich ist"*
- `e2e/jobs-journey.spec.ts:17` — *„eine veröffentlichte Stelle findet auch,
  wer kein Konto hat"*
- Der Zweck selbst: eine Ausschreibung, die man nur nach Anmeldung sieht,
  erreicht genau die nicht, für die sie gedacht ist.

## Was es heute kostet

`/careers/<kuerzel>` ist **halb** öffentlich. Ein anonymer Besucher sieht Name,
Beschreibung, Standorte und Leistungen des Unternehmens — der Abschnitt „Offene
Stellen" darunter zeigt ihm den Fehlerzweig. Die Seite existiert also für ihn,
nur ihr Inhalt nicht.

Und `/jobs` ist für ihn eine Suchmaske, die nie etwas findet.

## Die Frage, die zu entscheiden ist

**Sollen veröffentlichte Anzeigen ohne Konto lesbar sein?**

*Ja* hiesse: `GET /jobs` gibt anonym nur `published` heraus (Entwürfe und
geschlossene bleiben drinnen), die Routenkarte bekommt in der linken Spalte
200 statt 401 — und der CI-Job braucht einen anderen Beleg für die
Weiterleitung, denn seiner beruht gerade darauf, dass die Antwort 401 ist.

*Nein* hiesse: die Karriereseite zeigt für Anonyme keine Stellen, und der Satz
in der Routenkarte („weil die Karriereseite oeffentlich ist") gilt nur für das
Profil. Dann sind zwei E2E-Reisen falsch und gehören umgeschrieben, und
`CLAUDE.md`s Frontend-Abschnitt muss sagen, was „öffentlich" hier heisst.

**Beides ist vertretbar, und keins ist meine Entscheidung.** Bis dahin bleiben
die zwei Reisen rot — das ist ehrlicher, als sie auf den heutigen Zustand
umzuschreiben und den Widerspruch damit zuzudecken.

## Stand

- [ ] entschieden
- [ ] Routenkarte, CI-Beleg und CLAUDE.md auf die Entscheidung gebracht
- [ ] die zwei Reisen entsprechend
