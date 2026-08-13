# Befund & Soll — E3d: die Unternehmensseiten

`/company/jobs`, `/company/team`, `/company/profile`, `/company/transfers`,
`/invitation`. Fünf Seiten, auf denen **ein Unternehmen handelt** — und die
einzige Gruppe, in der eine Rolle (`admin` vs. `member`) etwas ändert.

Gemessen am 13.08.2026, vor der Umstellung.

## Ist-Stand

| Route | Zeilen | Tests | Ladezustand | Rolle zählt |
|---|---|---|---|---|
| `/company/jobs` | 290 | 14 | **fehlt** | nein |
| `/company/transfers` | 232 | **0** | vorhanden | nein |
| `/company/team` | 222 | 12 | **fehlt** | ja (versteckt) |
| `/company/profile` | 184 | 6 | vorhanden | nein |
| `/invitation` | 127 | 6 | vorhanden | — |

`company-new.tsx` ist mit E2.6 verschwunden — ein Unternehmen entsteht nur bei
der Registrierung (ADR-0019).

## Vier Funde, die keine Umstellung sind

### 1. Ein Ausfall wird zu „dieses Unternehmen gibt es nicht" — öffentlich

`getCompanyBySlug` gibt `null` zurück für **drei** verschiedene Lagen:

```ts
const res = await send(`/companies/by-slug/${encodeURIComponent(slug)}`);
if (!res.ok) return null;     // 404, aber auch 500 und 503
…
} catch { return null; }      // und kein Netz
```

`career.tsx` macht daraus: *„Diese Seite gibt es nicht. Unter dieser Adresse ist
kein Unternehmen hinterlegt."*

Das ist der **dritte** Fall desselben Musters (nach `isGranted` in E3b und
`getNotificationPreferences` in E3c) — und der erste, den **Fremde** sehen. Ein
Unternehmen gibt seinen Karriere-Link an Bewerber; companies-service stolpert;
der Empfänger liest, dass es die Firma nicht gibt.

Bitter dabei: In E3a habe ich auf **derselben Seite** den Schwesterfall behoben —
ein gescheiterter Stellen-Abruf sah aus wie „nichts ausgeschrieben". Den Abruf
des Unternehmens habe ich nicht angesehen.

### 2. Ein Ausfall macht aus dem Firmenprofil ein leeres Formular — und der nächste Klick speichert es

`getOwnCompanyProfile` gibt bei jedem Fehlschlag `null`. `toForm(null)` ist
`EMPTY`, und der Ladezustand ist zu diesem Zeitpunkt längst vorbei (`isPending`
ist falsch, die Abfrage *gelang* ja). Die Seite zeigt also ein leeres Formular,
das aussieht wie „noch nichts eingetragen".

Wer dann den Anzeigenamen tippt und speichert, schickt ein `PUT` mit **leerem**
Über-uns, leerer Website, leeren Standorten und leeren Benefits — und überschreibt
damit, was vorher dastand.

Das ist Muster für Muster der Fund aus E3c (`ALL_ON`), nur mit Firmendaten statt
Benachrichtigungen: **ein Client, der einen Fehlschlag in eine plausible
Voreinstellung verwandelt, verschiebt eine Entscheidung an die Stelle, die sie
nicht treffen darf.**

### 3. Zwei Listen ohne Ladezustand

`/company/jobs` und `/company/team` prüfen `query.isPending` **gar nicht**.
Solange die Liste unterwegs ist, greift kein Zweig:

- Bei den Stellen sieht man die Karte „Bestehende" **leer** — nicht einmal
  „Noch keine Stelle angelegt", weil auch dieser Zweig `jobs?.ok` verlangt.
- Bei der Mannschaft dasselbe.

Dieselbe Klasse wie die leere Karte in `applications.tsx` (E3a) und der Satz
„bislang hat niemand gefragt" in `/resume` (E3c). Es ist der dritte und vierte
Fall.

### 4. `/company/transfers` hat 232 Zeilen und keinen einzigen Komponententest

Gedeckt ist sie nur durch `transfer-journey.spec.ts` — eine E2E-Reise, die den
Stapel braucht und sich ohne ihn selbst überspringt. Auf einer Maschine ohne
Docker ist diese Seite **völlig ungedeckt**, und `make check` bleibt grün.

Was dort ungeprüft steht, ist nicht nebensächlich: welcher Knopf in welchem
Zustand erscheint, und vor allem, dass **„Abschließen" nur ohne nötige Freigabe**
angeboten wird — bei einer nötigen Freigabe schließt die Person selbst ab, weil
nur sie weiß, ob sie gehen darf.

## Zusagen, die kein Refactor anfassen darf

38 Testnamen. Die tragenden:

- **Ein Entwurf wird angelegt, nie eine veröffentlichte Stelle.** Veröffentlichen
  ist ein zweiter, eigener Schritt.
- **Für eine geschlossene Stelle gibt es gar nichts mehr** — keinen Weg zurück.
- **Fähigkeiten gehen als Liste**, nicht als Textzeile.
- Die KI-Hilfe formuliert die **eigene Anzeige** (ADR-0024): nur auf Knopfdruck,
  der Hinweis steht am Knopf, sie ist **nicht** der Absende-Knopf des Formulars
  in dem sie steht, und über vorhandenem Text warnt sie.
- **Eine Einladung sagt dasselbe, ob die Adresse ein Konto hat oder nicht** —
  sonst wäre sie ein Prüfwerkzeug für Mitgliedschaft.
- **Ein Token wird nie angezeigt**, und eine Einladung genau einmal eingelöst,
  auch über ein zweites Mounten hinweg.
- **„Verlassen" heißt es bei einem selbst, „Entfernen" bei anderen.**
- **Der letzte Admin kann nicht gehen** — und die Seite sagt, was zu tun ist.
- **Die Ablöse wird festgehalten, nicht bewegt**: diese Plattform führt kein Geld.
- **Abschließen nur ohne nötige Freigabe** (siehe oben).
- `/invitation`: ein anonymer Besucher wird **zur Anmeldung geschickt**, nicht der
  Link beschuldigt; eine Ablehnung kommt in den Worten des Servers; und das
  Wechseln ins Unternehmen passiert **nicht stillschweigend**.

## Entschieden (13.08.2026)

- **`getCompanyBySlug` und `getOwnCompanyProfile` liefern ein Ergebnis statt
  `null`.** „Gibt es nicht" und „nicht abrufbar" werden getrennt. `career.tsx`
  sagt bei einem Ausfall, dass es einer ist; `/company/profile` zeigt bei einem
  Ausfall **kein Formular** — man kann nicht bearbeiten, was man nicht lesen
  konnte, und ein leeres Formular wäre die Einladung, echte Daten zu
  überschreiben.
- **`getCompanyProfile` (die öffentliche) bleibt bei `null`.** Bewusst: beide
  Lagen führen dort zum selben richtigen Verhalten — die Stelle bleibt anonym.
  Eine Unterscheidung, die nichts ändert, wäre Code ohne Wirkung.
- **Zwei neue Routen**, wie in der Routenkarte vorgesehen:
  `/company/jobs/new` und `/company/team/invite`.
  **Kein `/company/jobs/$id/edit`**: die Liste bietet heute nur Veröffentlichen
  und Schließen, kein Bearbeiten — eine Route für ein Formular, das es nicht
  gibt, wäre eine Behauptung über Funktionsumfang.
- **`/company/transfers` bekommt Komponententests**, bevor sie umgestellt wird.
  Eine Umstellung ohne Netz unter 232 ungedeckten Zeilen ist genau die Übung, bei
  der eine Zusage still verschwindet.
- **Keine Rollenprüfung auf dem Server.** Sie fehlt (siehe „Bekannte Lücken" in
  `oberflaeche-erwartete-ansichten.md`), und sie gehört nicht in einen
  Umstellungsschnitt: das ist eine Änderung am Verhalten mehrerer Dienste. Die
  Oberfläche versteckt weiterhin und behauptet nichts anderes.
