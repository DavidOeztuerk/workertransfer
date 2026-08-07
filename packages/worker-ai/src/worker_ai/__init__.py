"""Die Naht zur KI: ein Entwurf, den ein Mensch anfordert und danach ändert.

Neu geschrieben nach **ADR-0024**. Die frühere Fassung hielt vier Anbieter,
Tool-Calling, Memory und Vektorsuche vorrätig — 246 Zeilen mit acht schweren
Abhängigkeiten, ohne einen einzigen Konsumenten, unbaubar unter Python 3.14 und
deshalb aus dem Workspace ausgeschlossen. Dieselbe Geschichte wie
`worker-files` (ADR-0021), und dieselbe Antwort: **eine Umsetzung, die wirklich
benutzt wird, statt vier, die niemand einschaltet.**

Was hier bewusst NICHT steht:

- **Kein Memory, keine Vektorsuche.** Beides speichert, und dieser Schnitt
  speichert nichts — weder den Prompt noch die Antwort.
- **Keine Bewertung, keine Zusammenfassung ÜBER einen Menschen.** Das Modell
  hilft jemandem zu sagen, was er sagen will; es sagt nichts über ihn
  (ADR-0022).
- **Kein Protokoll des Inhalts.** `product-scope.md` verbietet CVs und Verträge
  im Log; der Freitext einer Person gehört in dieselbe Klasse. Was hier
  fehlschlägt, wird ohne Prompt und ohne Antwort gemeldet.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Protocol

import httpx

__all__ = [
    "ANTHROPIC_MESSAGES_URL",
    "AnthropicDrafter",
    "DraftContext",
    "Draftable",
    "DrafterUnavailable",
    "JobDraftContext",
    "NullDrafter",
    "TextDrafter",
]

#: Kurz gehalten, absichtlich. Ein Entwurf, den man nicht auf einen Blick
#: prüfen kann, wird nicht geprüft, sondern übernommen.
MAX_OUTPUT_TOKENS = 700
TIMEOUT_SECONDS = 30.0
#: Wohin der Aufruf geht. Überschreibbar, damit ein Gateway oder ein Proxy
#: davorstehen kann — **kein Provider-Wechsel**: wer dort etwas hinstellt, das
#: anders antwortet als die Messages-API, bekommt `DrafterUnavailable`.
ANTHROPIC_MESSAGES_URL = "https://api.anthropic.com/v1/messages"


class DrafterUnavailable(Exception):
    """Der Anbieter hat nicht geantwortet — oder es ist keiner eingerichtet.

    Ein eigener Fehler und **kein stiller Rückfall auf eine Vorlage**: ein
    Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre die schlechtere
    Antwort. Die Meldung trägt nie den Prompt und nie die Antwort.
    """


@dataclass(frozen=True, slots=True)
class DraftContext:
    """Was in den Prompt darf — und damit auch: was nicht.

    Diese Klasse IST die Grenze. Sie trägt, was die Person selbst über sich
    geschrieben hat, und sonst nichts: **kein Name, keine E-Mail-Adresse, keine
    `subject_id`, kein Arbeitgeber, kein Lebenslauf, keine Bewerbung, kein
    Marktstatus.**

    Als Datenklasse und nicht als freies `dict`, weil ein `dict` beim nächsten
    Feature stillschweigend einen Schlüssel mehr trägt.
    """

    headline: str = ""
    bio: str = ""
    skills: tuple[str, ...] = field(default_factory=tuple)
    #: Was die Person will („kürzer", „sachlicher"). Freitext von ihr, an sie
    #: gerichtet.
    wish: str = ""

    @property
    def system(self) -> str:
        return _SYSTEM_PERSON

    @property
    def prompt(self) -> str:
        return build_prompt(self)


class Draftable(Protocol):
    """Was ein Zusammenhang können muss: sich selbst erklären.

    Jede Sorte Entwurf bringt ihre **eigenen Regeln** mit, statt einen
    gemeinsamen Prompt zu teilen. Das ist Absicht: die Regeln für einen
    Profiltext („schreibe in der Ich-Form", „erfinde nichts über die Person")
    und die für eine Stellenanzeige („schreibe für das Unternehmen", „keine
    Anforderungen dazuerfinden") haben nichts miteinander zu tun. Ein
    gemeinsamer Prompt mit Verzweigungen wäre die Stelle, an der irgendwann eine
    Regel für die falsche Seite gilt.
    """

    @property
    def system(self) -> str: ...

    @property
    def prompt(self) -> str: ...


class TextDrafter(Protocol):
    async def draft(self, context: Draftable) -> str: ...


class NullDrafter:
    """Kein Anbieter eingerichtet — die Funktion ist ehrlich aus.

    Die Voreinstellung. Eine, die im Zweifel einen fremden Dienst anruft, wäre
    die falsche: der Text einer Person verlässt die Plattform dann, weil
    jemand vergessen hat, etwas abzuschalten.
    """

    async def draft(self, context: Draftable) -> str:
        _ = context
        raise DrafterUnavailable("no drafting provider configured")


#: Was das Modell tun soll — und woran es sich zu halten hat.
#:
#: „Schreibe ALS die Person" ist der Kern: der Entwurf ist ein Vorschlag für
#: ihren eigenen Text, keine Beschreibung von außen. Deshalb auch das Verbot,
#: etwas hinzuzuerfinden — ein Profil, in dem eine Fähigkeit steht, die die
#: Person nie genannt hat, ist eine Falschaussage über sie, und sie merkt es
#: womöglich erst im Gespräch.
_SYSTEM_PERSON = (
    "Du hilfst einer Person, ihren eigenen Profiltext auf einer "
    "Job-Plattform zu formulieren. Schreibe in der Ich-Form, auf Deutsch, "
    "sachlich und ohne Werbesprache.\n"
    "Regeln:\n"
    "- Erfinde NICHTS hinzu. Benutze nur, was unten steht. Wenn dort wenig "
    "steht, schreibe wenig.\n"
    "- Keine Superlative, keine Behauptungen über Erfahrung in Jahren, keine "
    "Bewertung der Person.\n"
    "- Höchstens 120 Wörter.\n"
    "- Gib nur den Text zurück, ohne Anrede, ohne Überschrift, ohne "
    "Erklärung."
)


def build_prompt(context: DraftContext) -> str:
    """Der Prompt, wörtlich — damit ein Test ihn prüfen kann.

    Getrennt vom Versand, weil die interessante Frage nicht ist, ob HTTP
    funktioniert, sondern **was hinausgeht**.
    """
    parts = [
        f"Bisherige Überschrift: {context.headline}" if context.headline else "",
        f"Bisheriger Text: {context.bio}" if context.bio else "",
        f"Fähigkeiten: {', '.join(context.skills)}" if context.skills else "",
        f"Wunsch der Person: {context.wish}" if context.wish else "",
    ]
    return "\n".join(part for part in parts if part)


#: Der Company-Agent. Er hilft einem Unternehmen, **seine eigene Anzeige**
#: verständlicher zu schreiben — und sagt nichts über Menschen.
#:
#: Das ist der einzige Unternehmens-Agent aus dem ULTRAPLAN, der ohne Abwägung
#: gebaut werden kann. Scout, Candidate Ranking, Salary Recommendation und Team
#: Analyzer richten sich alle auf Personen; dieser richtet sich auf einen Text,
#: den das Unternehmen selbst verfasst hat.
_SYSTEM_JOB = (
    "Du hilfst einem Unternehmen, seine eigene Stellenanzeige verständlicher "
    "zu formulieren. Schreibe auf Deutsch, sachlich, ohne Werbesprache.\n"
    "Regeln:\n"
    "- Erfinde KEINE Anforderungen, Aufgaben oder Leistungen hinzu. Benutze "
    "nur, was unten steht.\n"
    "- Schreibe geschlechtsneutral und sprich Bewerbende direkt an.\n"
    "- Keine Superlative (\u201eWeltmarktf\u00fchrer\u201c, \u201eRockstar\u201c), keine "
    "Floskeln (\u201edynamisches Team\u201c).\n"
    "- Nenne keine Altersangaben, keine Herkunft, keine Familiensituation und "
    "nichts, was eine Personengruppe ausschließt.\n"
    "- Höchstens 250 Wörter.\n"
    "- Gib nur den Text der Beschreibung zurück, ohne Titel, ohne Erklärung."
)


@dataclass(frozen=True, slots=True)
class JobDraftContext:
    """Der Zusammenhang einer Stellenanzeige.

    Getrennt von `DraftContext` und nicht als gemeinsame Klasse mit
    Verzweigungen: hier steht, was ein UNTERNEHMEN über sich geschrieben hat,
    dort, was eine PERSON über sich geschrieben hat. Zwei Klassen können nicht
    versehentlich die Regeln der jeweils anderen bekommen.

    Kein `tenant_id`, kein Firmenname, keine Adresse: der Entwurf braucht sie
    nicht, und was nicht in der Klasse steht, kann nicht hinausgehen.
    """

    title: str = ""
    description: str = ""
    skills: tuple[str, ...] = field(default_factory=tuple)
    location: str = ""
    wish: str = ""

    @property
    def system(self) -> str:
        return _SYSTEM_JOB

    @property
    def prompt(self) -> str:
        parts = [
            f"Titel: {self.title}" if self.title else "",
            f"Bisherige Beschreibung: {self.description}" if self.description else "",
            f"Gesuchte Fähigkeiten: {', '.join(self.skills)}" if self.skills else "",
            f"Ort: {self.location}" if self.location else "",
            f"Wunsch: {self.wish}" if self.wish else "",
        ]
        return "\n".join(part for part in parts if part)


class AnthropicDrafter:
    """Der eine Anbieter.

    Ein Aufruf, ein Text. Keine Wiederholung: wer gedrückt hat, sieht einen
    Fehler und drückt noch einmal — ein stiller zweiter Versuch verdoppelt die
    Kosten und die Wartezeit, ohne dass jemand darum gebeten hat.
    """

    def __init__(
        self,
        *,
        api_key: str,
        model: str = "claude-sonnet-5",
        base_url: str = ANTHROPIC_MESSAGES_URL,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self._api_key = api_key
        self._model = model
        self._base_url = base_url
        self._client = client

    async def draft(self, context: Draftable) -> str:
        body = {
            "model": self._model,
            "max_tokens": MAX_OUTPUT_TOKENS,
            "system": context.system,
            "messages": [{"role": "user", "content": context.prompt}],
        }
        headers = {
            "x-api-key": self._api_key,
            "anthropic-version": "2023-06-01",
            "content-type": "application/json",
        }
        try:
            if self._client is not None:
                response = await self._client.post(self._base_url, json=body, headers=headers)
            else:
                async with httpx.AsyncClient(timeout=TIMEOUT_SECONDS) as client:
                    response = await client.post(self._base_url, json=body, headers=headers)
        except httpx.HTTPError as exc:
            # Nur die Fehlerart, nie der Inhalt: der Prompt trägt den Freitext
            # einer Person und gehört nicht ins Protokoll.
            raise DrafterUnavailable(f"provider unreachable ({type(exc).__name__})") from None

        if response.status_code != 200:
            raise DrafterUnavailable(f"provider answered {response.status_code}")

        return _text_of(response.json())


def _text_of(payload: object) -> str:
    """Liest den Text aus der Antwort — und beschwert sich, statt zu raten.

    Eine leere Zeichenkette zurückzugeben, wenn die Antwort anders aussieht als
    erwartet, hieße: die Person drückt, es passiert nichts, und niemand weiß
    warum.
    """
    if not isinstance(payload, dict):
        raise DrafterUnavailable("provider answered in an unexpected shape")
    blocks = payload.get("content")
    if not isinstance(blocks, list):
        raise DrafterUnavailable("provider answered in an unexpected shape")
    texts = [
        block["text"]
        for block in blocks
        if isinstance(block, dict) and isinstance(block.get("text"), str)
    ]
    if not texts:
        raise DrafterUnavailable("provider answered without text")
    return "\n".join(texts).strip()
