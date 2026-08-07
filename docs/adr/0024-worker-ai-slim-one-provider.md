# ADR-0024 — `worker-ai` neu geschrieben: eine Naht, ein Anbieter, nichts gespeichert

Date: 2026-08-05
Status: Angenommen
Related: ADR-0021 (`worker-files` gelöscht, `worker-storage` schlank), ADR-0022 (keine Zahl über einen Menschen), ADR-0023 (Vokabular benennt um), [Entwurf](../superpowers/specs/2026-08-05-ai-seam-design.md), [product-scope](../product-scope.md)

## Zusammenhang

`worker-ai` bestand aus 246 Zeilen mit **acht** Abhängigkeiten: `openai`,
`anthropic`, `google-generativeai`, `ollama`, `pydantic-ai`, `chromadb`,
`sentence-transformers`, `pydantic`. Es hatte einen Smoke-Test und **keinen
Konsumenten**. Wegen der ML-Wheels ohne Python-3.14-Build war es aus dem
uv-Workspace **und** aus mypy ausgeschlossen — der einzige verbliebene Skip in
der Testreihe.

Das ist Wort für Wort die Lage von `worker-files` vor ADR-0021: eine Hülle, die
so viel vorrätig hielt, dass sie unbaubar wurde, und deshalb nie lief.

## Entscheidung

Neu geschrieben mit **einer** Abhängigkeit (`httpx`), **einem** Anbieter und
**einem** echten Konsumenten. Zurück im Workspace, zurück in mypy, kein Skip
mehr.

```
TextDrafter (Port)     async def draft(context: DraftContext) -> str
AnthropicDrafter       der eine Anbieter
NullDrafter            kein Schlüssel: die Funktion ist ehrlich aus
DraftContext           WAS hinausgeht — und damit auch, was nicht
```

**`httpx` statt eines Anbieter-SDK.** Der Aufruf ist ein POST mit JSON. Ein SDK
brächte einen eigenen Auth-, Retry- und Typ-Apparat mit, von dem hier nichts
gebraucht wird — und `httpx` steht ohnehin in jedem Dienst.

**Ein Anbieter statt vier.** Vier Provider-Klassen wären vier Umsetzungen, von
denen höchstens eine je gelaufen ist. Der Port ist die Naht; ein zweiter
Anbieter kommt, wenn eine Umgebung ihn braucht.

**`NullDrafter` ist die Voreinstellung.** Ohne Schlüssel wird kein fremder
Dienst angerufen. Eine Voreinstellung, die im Zweifel anruft, würde den Text
einer Person hinausschicken, weil jemand vergessen hat, etwas abzuschalten.

## Die vier Regeln, die den Schnitt tragen

**1. Nur auf Anforderung.** Kein Hintergrundlauf, kein Vorschlag von selbst.
Dieselbe Regel wie bei GitHub (6.1): einmal auf Bitte hinsehen ist etwas
anderes als dauerhaft hinterhersehen.

**2. Es wird nichts gespeichert.** Nicht der Prompt, nicht die Antwort. Der
Entwurf lebt im Formular, bis die Person speichert — und dann ist es **ihr**
Text. Kein Feld „von KI erzeugt", keine Historie, keine Kennzahl. Was es nicht
gibt, kann auch nicht ausgewertet werden.

**3. `DraftContext` ist die Grenze.** Es trägt Überschrift, Freitext,
Fähigkeiten und den Wunsch der Person — **kein Name, keine E-Mail-Adresse,
keine `subject_id`, kein Arbeitgeber, kein Lebenslauf, keine Bewerbung, kein
Marktstatus.** Eine Datenklasse und kein freies `dict`, weil ein `dict` beim
nächsten Feature stillschweigend einen Schlüssel mehr trägt. Ein Test nagelt
die Feldmenge fest.

**4. Nichts davon im Protokoll.** `product-scope.md` verbietet CVs und Verträge
im Log; der Selbstbeschreibungstext einer Person gehört in dieselbe Klasse.
Fehler melden die Fehlerart, nie den Inhalt — ein Test prüft, dass ein
Netzwerkfehler den Prompt nicht mitschleppt.

## Was das Modell tun soll — und was nicht

Der System-Prompt steht im Code und wird von Tests geprüft, weil er die
Entscheidung trägt und nicht nur eine Formulierung ist:

- **Ich-Form.** Der Entwurf ist ein Vorschlag für den Text der Person, keine
  Beschreibung von außen.
- **„Erfinde NICHTS hinzu."** Ein Profil, in dem eine Fähigkeit steht, die die
  Person nie genannt hat, ist eine Falschaussage über sie — und sie merkt es
  womöglich erst im Gespräch.
- **„Keine Bewertung der Person."** ADR-0022 gilt auch für Sätze, nicht nur für
  Zahlen.

## Wo der Konsument liegt

`POST /profiles/me/draft` in `profile-service`, nicht in einem neuen Dienst.

Ein eigener Dienst für einen Endpunkt wäre wieder eine Hülle. Die Daten liegen
ohnehin dort, und ein Aufruf über die Dienstgrenze bräuchte eine zweite
Consent-Prüfung, die von der ersten abweichen kann. Geteilt ist das **Paket**,
nicht ein Vermittler: jeder Dienst, der später einen Entwurf braucht
(Lebenslauf, Portfolio), hängt sich an dieselbe Naht.

Der Aufruf steht auf einem **eigenen** Endpunkt: `GET /profiles/me` ruft nie
einen Anbieter. Sonst wäre die Profilseite so schnell wie der langsamste fremde
Dienst.

## Folgen

- Ein Skip weniger; die Testreihe hat keinen mehr.
- `WORKER_ANTHROPIC_API_KEY` ist eine reine Umgebungsvariable. Leer heißt: die
  Funktion ist aus. Der Schlüssel steht nie im Repository und nie im Log.
- **Das E2E kann den echten Anbieter nicht prüfen** — es bräuchte ein Geheimnis
  und würde Geld kosten. Geprüft wird der Weg bis zur Naht und der Zustand
  „nicht eingerichtet", der in jeder Umgebung ohne Schlüssel der echte ist.
- Was hier NICHT wiederkommt: Memory, Vektorsuche, Tool-Calling,
  plan-act-reflect. Alles davon speichert oder entscheidet; beides ist in
  diesem Schnitt ausgeschlossen. Sie kommen, wenn ein Agent sie braucht — nicht
  vorrätig.

## Verworfene Möglichkeiten

- **`worker-ai` reparieren.** Der Import ließe sich mit `--extra` retten. Dann
  stünden acht Abhängigkeiten scharf, von denen sieben niemand aufruft — und
  der Python-3.14-Ausschluss bliebe.
- **`apps/ai-service`.** Ein Dienst mit Container und Health-Check für einen
  Endpunkt, der nichts speichert.
- **Vier Provider hinter dem Port.** Der Port ist die Naht; mehr als eine
  Umsetzung ohne Umgebung, die sie braucht, ist genau ADR-0021s Fehler.
