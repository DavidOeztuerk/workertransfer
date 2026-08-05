# Phase 7, Sub-step 7.1 — Die Naht zur KI: ein Entwurf, den die Person anfordert

Date: 2026-08-05
Status: Entwurf (selbst geprüft)
Related: **ADR-0024** (`worker-ai` schlank), ADR-0021 (schlank statt vorrätig), ADR-0022 (keine Zahl über einen Menschen), ADR-0013 (Consent-Ledger), [product-scope](../../product-scope.md), [ULTRAPLAN](../../ULTRAPLAN.md) Phase 7

## Der Zustand, auf dem das hier aufsetzt

`worker-ai`: 246 Zeilen, **acht** schwere Abhängigkeiten (openai, anthropic,
google-generativeai, ollama, pydantic-ai, chromadb, sentence-transformers,
pydantic), ein Smoke-Test, **kein Konsument**. Aus dem uv-Workspace und aus
mypy ausgeschlossen, weil die ML-Wheels keinen Python-3.14-Build haben.

Das ist Wort für Wort die Lage von `worker-files` vor ADR-0021: eine Hülle, die
so viel vorrätig hielt, dass sie unbaubar wurde — und deshalb nie lief. Die
Antwort ist dieselbe: **eine Umsetzung, die wirklich benutzt wird, statt vier,
die niemand einschaltet.**

## Was der ULTRAPLAN wollte — und was davon zuerst kommt

Phase 7 nennt 23 Agenten, vier Provider, Memory in vier Sorten, Vektorsuche,
plan-act-reflect. Das ist keine erste Stufe, das ist die Phase.

Und mehrere der genannten Agenten sind nach ADR-0022 gar nicht baubar:
**Candidate Ranking**, **Salary Recommendation**, **Team Analyzer**,
**Skill Analyzer** sind allesamt Zahlen oder Rangfolgen über Menschen. Sie
stehen hier nicht auf der Warteliste, sie stehen auf keiner.

Was zuerst kommt, ist die **Naht** und **ein** Agent, an dem sie sich beweist.

## Der erste Agent: die Person schreibt über sich, die KI hilft beim Formulieren

> „Was soll ich über mich schreiben?"

Das ist die Stelle, an der ein leeres Profil leer bleibt. Die Person drückt
einen Knopf, bekommt **einen Entwurf** und ändert ihn. Gespeichert wird nur,
was sie danach selbst speichert.

**Warum ausgerechnet dieser.** Die These der Plattform ist, dass ein Mensch
seine Darstellung selbst kontrolliert. Eine KI, die *über* ihn schreibt —
aus Commits, aus dem Lebenslauf, aus Signalen — verletzt das. Eine KI, die ihm
hilft zu sagen, **was er sagen will**, bedient es.

Der Unterschied ist nicht Geschmack: im ersten Fall entsteht eine Aussage, der
niemand zugestimmt hat, im zweiten ein Vorschlag, den jemand angefordert hat.

## Die vier Regeln dieses Schnitts

**1. Nur auf Anforderung.** Kein Hintergrundlauf, keine Vorschläge von selbst.
Dieselbe Regel wie bei GitHub (6.1): eine Plattform, die einem Menschen
dauerhaft hinterhersieht, tut etwas anderes als eine, die einmal auf seine
Bitte hinsieht.

**2. Es wird nichts gespeichert.** Nicht der Prompt, nicht die Antwort. Der
Entwurf lebt im Formular, bis die Person speichert — und dann ist es **ihr**
Text, nicht der eines Modells. Kein Feld „von KI erzeugt", keine Statistik,
keine Historie. Was es nicht gibt, kann auch nicht ausgewertet werden.

**3. Es steht dabei, was hinausgeht.** Der Text geht an einen fremden Anbieter.
Das ist kein Detail und wird nicht in eine Datenschutzerklärung verschoben: es
steht an dem Knopf, den die Person drückt, und nennt den Anbieter.

**4. Nichts davon sieht ein Unternehmen.** Kein Hinweis, dass ein Text mit
Hilfe entstanden ist. Ein solcher Hinweis wäre ein Merkmal, nach dem sortiert
werden kann.

## Was NICHT hinausgeht

Der Prompt trägt: die Überschrift und den Freitext, die schon im Profil stehen,
die Fähigkeiten, und den Wunsch der Person („kürzer", „sachlicher").

Er trägt **nicht**: Name, E-Mail-Adresse, `subject_id`, Arbeitgeber,
Lebenslauf, Bewerbungen, Marktstatus. Ein Test hält das fest — und zwar gegen
den zusammengebauten Prompt, nicht gegen die Absicht.

Und: **nichts davon wird protokolliert.** `product-scope.md` verbietet CVs und
Verträge im Log; ein Prompt mit dem Freitext einer Person gehört in dieselbe
Klasse.

## Die Naht

```
TextDrafter (Port)
    async def draft(instruction, context) -> str

AnthropicDrafter   — der eine Anbieter, eine Abhängigkeit
NullDrafter        — kein Schlüssel konfiguriert: die Funktion ist ehrlich aus
```

**Ein Anbieter, nicht vier.** Vier Provider-Klassen wären vier Umsetzungen, von
denen höchstens eine je gelaufen ist — genau der Fehler, den ADR-0021 beschreibt.
Der Port ist die Naht; ein zweiter Anbieter kommt, wenn eine Umgebung ihn
braucht.

**`NullDrafter` statt eines Absturzes.** Ohne Schlüssel ist die Funktion aus,
und die Oberfläche sagt das. Eine Voreinstellung, die im Zweifel einen fremden
Dienst anruft, wäre die falsche.

## Wo der Endpunkt liegt

`POST /profiles/me/draft` — in `profile-service`, nicht in einem neuen Dienst.

Ein eigener Dienst für einen Endpunkt wäre wieder eine Hülle. Die Daten, die
der Entwurf braucht, liegen ohnehin hier, und ein Aufruf über die Dienstgrenze
hätte eine zweite Consent-Prüfung nötig, die von der ersten abweichen kann.

Die geteilte Naht ist das **Paket**, nicht der Dienst: `worker-ai` steht jedem
Dienst zur Verfügung, der später einen Entwurf braucht (Lebenslauf, Portfolio),
ohne dass es einen Vermittler gibt.

**Der Aufruf nach außen darf keinen Lesepfad blockieren.** Er steht auf einem
eigenen Endpunkt, und `GET /profiles/me` ruft nie einen Anbieter.

## Abgrenzung

**Kein plan-act-reflect.** Eine Schleife lohnt, wenn ein Agent Werkzeuge
benutzt und Zwischenergebnisse prüft. Hier gibt es einen Aufruf und einen Text,
den ein Mensch liest — die Prüfung ist der Mensch.
**Kein Memory, keine Vektorsuche.** Beides speichert, und dieser Schnitt
speichert nichts.
**Kein Unternehmens-Agent.** Die DoD von Phase 7 verlangt einen; die vier, die
der ULTRAPLAN nennt, sind alle Zahlen über Menschen. Ein zulässiger
Unternehmens-Agent (etwa: eine Ausschreibung verständlicher formulieren) ist
denkbar und gehört in einen eigenen Schnitt, nicht nebenbei hier hinein.
**Kein Vertragsentwurf.** Phase 8, und dort erst nach den Rechtsfragen.

## Selbstprüfung

*Ist ein einzelner Agent zu wenig für eine Phase?* Für die Phase ja, für den
ersten Schnitt nein. Die Naht ist das, was die anderen 22 tragen muss — und
sie ist an einem Agenten prüfbar, an dem nichts kaputtgehen kann.

*Was, wenn der Anbieter nicht antwortet?* Ein ehrlicher Fehler an genau der
Stelle, an der jemand gedrückt hat. Kein halber Text, kein stiller Rückfall auf
eine Vorlage — ein Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre
die schlechtere Antwort.

*Kann das E2E den echten Anbieter prüfen?* Nein, und das steht auch so da: es
bräuchte ein Geheimnis und würde Geld kosten. Geprüft wird der Weg bis zur
Naht und der Zustand „nicht eingerichtet" — der in jeder Umgebung ohne
Schlüssel der echte ist.
