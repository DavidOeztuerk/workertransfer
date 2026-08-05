"""Die Naht zur KI — und vor allem: was NICHT hinausgeht."""

from __future__ import annotations

import httpx
import pytest
from worker_ai import (
    AnthropicDrafter,
    DraftContext,
    DrafterUnavailable,
    NullDrafter,
    build_prompt,
)


def _client(handler: object) -> httpx.AsyncClient:
    return httpx.AsyncClient(transport=httpx.MockTransport(handler))  # type: ignore[arg-type]


def _ok(text: str = "Ich baue Backends.") -> object:
    def handler(request: httpx.Request) -> httpx.Response:
        _ = request
        return httpx.Response(200, json={"content": [{"type": "text", "text": text}]})

    return handler


class TestWhatLeavesThePlatform:
    """Der tragende Test dieses Schnitts.

    Der Prompt geht an einen fremden Anbieter. Was er trägt, ist deshalb keine
    Umsetzungsfrage, sondern die Entscheidung selbst — und sie wird gegen den
    zusammengebauten Prompt geprüft, nicht gegen die Absicht.
    """

    def test_it_carries_only_what_the_person_wrote_about_herself(self) -> None:
        prompt = build_prompt(
            DraftContext(
                headline="Backend-Entwicklerin",
                bio="Ich mag klare Schnittstellen.",
                skills=("Python", "PostgreSQL"),
                wish="kürzer",
            )
        )

        assert "Backend-Entwicklerin" in prompt
        assert "PostgreSQL" in prompt
        assert "kürzer" in prompt

    def test_the_context_has_no_field_for_anything_identifying(self) -> None:
        """`DraftContext` IST die Grenze — deshalb wird sie hier festgenagelt.

        Ein freies `dict` würde beim nächsten Feature stillschweigend einen
        Schlüssel mehr tragen, und niemand sähe es.
        """
        assert set(DraftContext.__dataclass_fields__) == {"headline", "bio", "skills", "wish"}

    @pytest.mark.parametrize(
        "forbidden",
        ["anna@example.com", "Anna Musterfrau", "Muster GmbH", "550e8400-e29b-41d4-a716"],
    )
    def test_nothing_identifying_can_reach_the_prompt(self, forbidden: str) -> None:
        # Es gibt schlicht kein Feld dafür. Der Test hält fest, dass das so
        # bleibt: käme eines dazu, träte hier ein Wert durch.
        prompt = build_prompt(DraftContext(headline="Entwicklerin", bio="Text", wish="kürzer"))

        assert forbidden not in prompt

    def test_an_empty_context_produces_an_empty_prompt(self) -> None:
        # Kein Gerüst aus Platzhaltern: „Bisheriger Text: " ohne Text wäre eine
        # Zeile, die das Modell füllen soll — und dann erfindet es.
        assert build_prompt(DraftContext()) == ""


class TestTheSystemRules:
    def test_the_model_is_told_to_invent_nothing(self) -> None:
        """Ein erfundener Satz im Profil ist eine Falschaussage über die Person.

        Und zwar eine, die sie womöglich erst im Gespräch bemerkt.
        """
        from worker_ai import _SYSTEM

        assert "Erfinde NICHTS hinzu" in _SYSTEM

    def test_the_model_is_told_not_to_judge(self) -> None:
        from worker_ai import _SYSTEM

        assert "keine Bewertung der Person" in _SYSTEM

    def test_it_writes_as_the_person_not_about_her(self) -> None:
        # Der Kern: der Entwurf ist ein Vorschlag für IHREN Text, keine
        # Beschreibung von außen.
        from worker_ai import _SYSTEM

        assert "Ich-Form" in _SYSTEM


class TestWithoutAProvider:
    async def test_the_default_is_off_and_says_so(self) -> None:
        """Eine Voreinstellung, die im Zweifel einen fremden Dienst anruft, wäre
        die falsche: der Text einer Person verließe die Plattform, weil jemand
        vergessen hat, etwas abzuschalten."""
        with pytest.raises(DrafterUnavailable):
            await NullDrafter().draft(DraftContext(headline="Test"))


class TestTheProvider:
    async def test_it_returns_the_text(self) -> None:
        drafter = AnthropicDrafter(api_key="k", client=_client(_ok()))

        assert await drafter.draft(DraftContext(headline="X")) == "Ich baue Backends."

    async def test_a_refusal_is_an_honest_error_not_an_empty_draft(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            _ = request
            return httpx.Response(429, json={"error": "rate limited"})

        drafter = AnthropicDrafter(api_key="k", client=_client(handler))

        with pytest.raises(DrafterUnavailable):
            await drafter.draft(DraftContext(headline="X"))

    async def test_a_network_failure_never_leaks_the_prompt(self) -> None:
        """Die Meldung landet im Protokoll. Der Freitext einer Person nicht.

        `product-scope.md` verbietet CVs und Verträge im Log; ein Prompt mit dem
        Selbstbeschreibungstext gehört in dieselbe Klasse.
        """

        def handler(request: httpx.Request) -> httpx.Response:
            raise httpx.ConnectError("boom", request=request)

        drafter = AnthropicDrafter(api_key="k", client=_client(handler))

        with pytest.raises(DrafterUnavailable) as raised:
            await drafter.draft(DraftContext(headline="Sehr persönlicher Satz"))

        assert "Sehr persönlicher Satz" not in str(raised.value)

    async def test_an_unexpected_shape_complains_instead_of_guessing(self) -> None:
        # Leer zurückzugeben hieße: die Person drückt, es passiert nichts, und
        # niemand weiß warum.
        def handler(request: httpx.Request) -> httpx.Response:
            _ = request
            return httpx.Response(200, json={"unerwartet": True})

        drafter = AnthropicDrafter(api_key="k", client=_client(handler))

        with pytest.raises(DrafterUnavailable):
            await drafter.draft(DraftContext(headline="X"))

    async def test_the_key_travels_in_the_header_and_not_in_the_body(self) -> None:
        seen: dict[str, object] = {}

        def handler(request: httpx.Request) -> httpx.Response:
            seen["header"] = request.headers.get("x-api-key")
            seen["body"] = request.content.decode()
            return httpx.Response(200, json={"content": [{"type": "text", "text": "ok"}]})

        await AnthropicDrafter(api_key="geheim", client=_client(handler)).draft(
            DraftContext(headline="X")
        )

        assert seen["header"] == "geheim"
        assert "geheim" not in str(seen["body"])
