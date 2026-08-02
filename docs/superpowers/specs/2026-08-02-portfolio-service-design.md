# Sub-step 3.4 — Portfolio-Service: das Schaufenster, nicht der Aktenschrank

Date: 2026-08-02
Status: Entwurf (selbst geprüft)
Related: ADR-0013 (Consent-Ledger), ADR-0020 (Consent als Enabler in der Praxis), [Profile-Design](2026-08-01-profile-service-design.md), [Resume-Design](2026-08-02-resume-service-design.md)

## Die offene Frage aus 3.3

Zwei Freigabemodelle stehen bereit, und das Portfolio muss sich für eines entscheiden:

- **wie das Profil** — eine Freigabe für alle Unternehmen (`portfolio.visibility:public`)
- **wie der Lebenslauf** — je Unternehmen einzeln, auf Anfrage

**Entscheidung: wie das Profil.**

Der Grund liegt darin, was ein Portfolio ist. Ein Lebenslauf nennt Arbeitgeber
und Zeiträume — Tatsachen über die Vergangenheit, die der aktuelle Arbeitgeber
nicht sehen soll. Ein Portfolio zeigt, was jemand gemacht hat, und ist
**absichtlich** ein Schaufenster: man legt hinein, was man zeigen will.

Daraus folgt eine klare Linie: **was nicht gezeigt werden darf, gehört nicht ins
Portfolio.** Ein internes Projekt unter Verschwiegenheit ist kein Portfolio-Stück
mit besonderer Sichtbarkeit, sondern schlicht keines. Diese Linie ist leichter
richtig zu treffen als eine Sichtbarkeitsstufe je Eintrag — und eine
Feinsteuerung, die niemand versteht, ist gefährlicher als keine.

Verworfen: **Sichtbarkeit je Eintrag.** Klingt mächtiger, verlagert aber die
Verantwortung für eine schwierige Entscheidung in ein Formularfeld, das beim
zwölften Eintrag niemand mehr bewusst setzt. Wer eine Abstufung braucht, kann
sie später additiv bekommen — genau wie ADR-0020 es für feinere Capabilities
vorsieht.

## Warum ein eigener Dienst

Er sieht dem Profile-Service ähnlich, und der Gedanke, das Portfolio einfach
dort anzuhängen, liegt nahe. Dagegen sprechen zwei Dinge:

1. Ein Portfolio-Eintrag bekommt in Sub-step 3.5 **Dateien** (Bilder, PDFs) —
   Speicher, Größenbegrenzungen, Virenprüfung, Ablaufregeln. Das ist eine andere
   Betriebslast als ein Textprofil, und sie in denselben Dienst zu legen hieße,
   das Profil an ihr mithaften zu lassen.
2. ADR-0004: eigene Datenbank je Dienst. Ein Portfolio wächst anders als ein
   Profil (viele Einträge je Person statt einer Zeile).

**In diesem Schnitt gibt es noch keine Dateien.** `worker-files`/`worker-storage`
sind aus dem Workspace ausgeschlossen, weil ihre Wheels für Python 3.14 fehlen
(Sub-step 3.5). Das Portfolio ist deshalb zunächst **Text und Links** — was für
Entwicklerinnen, Autorinnen und alle mit einer öffentlichen Arbeitsspur bereits
den größten Teil ausmacht.

## Domäne

**Ein Portfolio je Person**, `subject_id` ist der Schlüssel — wie Profil und
Lebenslauf. Darin bis zu 30 Einträge:

```
PortfolioItem
  title        ≤ 160, Pflicht
  summary      ≤ 1000        was es ist und was du daran gemacht hast
  url          ≤ 2000, optional, nur http/https
  role         ≤ 160         deine Rolle daran
  year         1900..aktuelles Jahr + 1, optional
```

**Nur `http` und `https`.** Ein Portfolio-Link wird von fremden Menschen
angeklickt. `javascript:` und `data:` sind in einem Feld, das später in einem
Browser landet, keine exotischen Randfälle, sondern der Normalfall eines
Angriffs.

**Kein `sort_order`.** Reihenfolge ist die Eingabereihenfolge — anders als beim
Lebenslauf, wo die Chronologie aus den Daten kommt. Ein Portfolio hat keine
natürliche Ordnung: „das hier zuerst" ist eine Entscheidung, und die trifft die
Person, indem sie die Liste anordnet.

**`year` ist optional und darf ein Jahr in der Zukunft sein** (bis zum nächsten):
etwas kann gerade erscheinen. Weiter in die Zukunft ist ein Tippfehler.

## Endpunkte

| Methode | Pfad | Wer | Antwort |
|---|---|---|---|
| `PUT` | `/portfolios/me` | jede angemeldete Person | das gespeicherte Portfolio |
| `GET` | `/portfolios/me` | dieselbe | Portfolio oder `null` |
| `GET` | `/portfolios/{subject_id}` | Unternehmen | Portfolio, wenn freigegeben |

Statuscodes nach ADR-0020, unverändert: `404` für „nicht vorhanden ODER nicht
freigegeben" (ununterscheidbar), `403` ohne aktives Unternehmen, `503` wenn der
Ledger schweigt, `null` für „noch keines angelegt".

Keine Liste über alle Portfolios: die Kandidatensuche läuft über das Profil, und
ein zweiter Listenendpunkt wäre ein zweiter Weg, dieselbe Menge zu erfragen —
mit dem Risiko, dass die beiden Filter auseinanderlaufen.

## Abgrenzung

**Keine Dateien** (Sub-step 3.5). **Kein Import** aus GitHub o. ä. — ADR-0004
verbietet Scraping, und eine offizielle API wäre ein eigener Schnitt.
**Keine Vorschaubilder**: sie abzurufen hieße, fremde URLs vom Server aus
aufzurufen, und das ist ein serverseitiger Anfrage-Fälschungsvektor, den man
nicht nebenbei einbaut.

## Umsetzung in zwei Scheiben

**Scheibe A** — Dienst über `worker new-service`, Domäne, Migration, Repository,
`PUT`/`GET /portfolios/me`, `GET /portfolios/{subject_id}` mit Consent-Gate.
Integrationstest über beide Dienste: freigeben → `200` → widerrufen → `404`.

**Scheibe B** — Oberfläche: `/portfolio` zum Pflegen, und auf der Kandidatenkarte
ein Weg zum freigegebenen Portfolio. Der Freigabeschalter sitzt bei den anderen
auf `/profile`, weil er dieselbe Frage beantwortet wie der dortige.

## Selbstprüfung

*Ist „ein Schalter für alles" nicht zu grob?* Für ein Schaufenster ja, und das
ist der Punkt: die Feinheit steckt in der Entscheidung, was hineinkommt, nicht
in einer Matrix danach.

*Warum liegt der Portfolio-Schalter auf `/profile`?* Weil beide dieselbe Frage
beantworten — „bin ich ansprechbar?" — und zwei Schalter auf zwei Seiten die
Person zwingen würden, sich zu merken, welcher was tut. Die Capability bleibt
getrennt (`portfolio.visibility:public`), damit sie einzeln widerrufbar ist.
