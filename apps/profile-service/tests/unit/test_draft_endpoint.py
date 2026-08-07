"""Der Entwurfs-Endpunkt — und die Frage, die zählt: was verlässt die Plattform?

Der Weg selbst ist unspektakulär: Profil lesen, Anbieter fragen, Text
zurückgeben. Interessant ist, was dabei NICHT passiert.
"""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any
from uuid import uuid4

import pytest
from fastapi import FastAPI
from httpx import ASGITransport, AsyncClient
from profile_service.domain.profile import Profile, Skills
from profile_service.presentation.http.router import build_router
from worker_ai import DraftContext, DrafterUnavailable

NOW = datetime(2026, 8, 5, tzinfo=UTC)
SUBJECT = uuid4()


class FakeRepo:
    def __init__(self, profile: Profile | None) -> None:
        self._profile = profile

    async def get(self, subject_id: Any) -> Profile | None:
        _ = subject_id
        return self._profile


class RecordingDrafter:
    """Merkt sich, WAS gefragt wurde. Das ist der Prüfgegenstand."""

    def __init__(self, answer: str = "Ich baue Backends.") -> None:
        self.answer = answer
        self.seen: DraftContext | None = None

    async def draft(self, context: DraftContext) -> str:
        self.seen = context
        return self.answer


class BrokenDrafter:
    async def draft(self, context: DraftContext) -> str:
        _ = context
        raise DrafterUnavailable("provider unreachable (ConnectError)")


def _profile() -> Profile:
    return Profile.create(
        subject_id=SUBJECT,
        headline="Backend-Entwicklerin",
        bio="Ich mag klare Schnittstellen.",
        location="Berlin",
        remote_ok=True,
        skills=Skills(["Python", "postgres"]),
        now=NOW,
    )


class _Principal:
    sub = SUBJECT
    tenant_id = None
    roles = ("user",)


def _app(drafter: Any, profile: Profile | None = None) -> FastAPI:
    from contextlib import asynccontextmanager

    @asynccontextmanager
    async def request_scope(_factory: Any) -> Any:
        yield None, {"profiles": FakeRepo(profile)}

    app = FastAPI()
    app.include_router(
        build_router(
            {
                "session_factory": None,
                "request_scope": request_scope,
                "drafter": drafter,
                "consent": None,
                "clock": None,
            }
        )
    )

    # Der Prinzipal kommt sonst aus der Auth-Middleware; hier wird genau die
    # eine Stelle gesetzt, die der Router liest.
    @app.middleware("http")
    async def _principal_middleware(request: Any, call_next: Any) -> Any:
        request.scope.setdefault("state", {})["user"] = _Principal()
        return await call_next(request)

    return app


async def _post(app: FastAPI, wish: str = "") -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.post("/profiles/me/draft", json={"wish": wish})


class TestWhatLeavesThePlatform:
    async def test_the_prompt_is_built_from_the_stored_profile_not_the_request(self) -> None:
        """Der Client kann den Zusammenhang nicht bestimmen.

        Was er nicht senden kann, kann er nicht in einen fremden Dienst
        schleusen — dieselbe Regel wie beim Tenant.
        """
        drafter = RecordingDrafter()

        await _post(_app(drafter, _profile()), wish="kürzer")

        assert drafter.seen is not None
        assert drafter.seen.headline == "Backend-Entwicklerin"
        assert drafter.seen.bio == "Ich mag klare Schnittstellen."
        assert drafter.seen.wish == "kürzer"

    async def test_the_subject_id_never_travels(self) -> None:
        drafter = RecordingDrafter()

        await _post(_app(drafter, _profile()), wish="kürzer")

        assert str(SUBJECT) not in repr(drafter.seen)

    async def test_the_skills_travel_canonicalised(self) -> None:
        # Nebenbei ein Beleg, dass das Vokabular (ADR-0023) auch hier greift:
        # das Profil wurde mit „postgres" angelegt.
        drafter = RecordingDrafter()

        await _post(_app(drafter, _profile()), wish="")

        assert drafter.seen is not None
        assert "PostgreSQL" in drafter.seen.skills


class TestNothingIsStored:
    async def test_the_answer_is_returned_and_not_written_to_the_profile(self) -> None:
        """Ein Vorschlag, kein Ergebnis.

        Gespeichert wird nur, was die Person danach selbst speichert — und dann
        ist es ihr Text, nicht der eines Modells.
        """
        profile = _profile()
        drafter = RecordingDrafter("Ein ganz anderer Satz.")

        response = await _post(_app(drafter, profile), wish="")

        assert response.status_code == 200
        assert response.json()["draft"] == "Ein ganz anderer Satz."
        # Das Aggregat ist unberührt.
        assert profile.headline == "Backend-Entwicklerin"
        assert profile.bio == "Ich mag klare Schnittstellen."


class TestWhenItIsNotAvailable:
    async def test_a_silent_provider_is_503_and_not_500(self) -> None:
        # Die Anfrage war in Ordnung. 503 heißt „später noch einmal", 500 hieße
        # „hier ist etwas kaputt" — und „kein Schlüssel eingerichtet" ist von
        # außen dasselbe wie „Anbieter antwortet nicht".
        response = await _post(_app(BrokenDrafter(), _profile()))

        assert response.status_code == 503

    async def test_the_refusal_never_carries_the_prompt(self) -> None:
        response = await _post(_app(BrokenDrafter(), _profile()))

        assert "klare Schnittstellen" not in response.text
        assert "ConnectError" not in response.text


class TestWithoutAProfile:
    async def test_someone_with_no_profile_still_gets_a_draft(self) -> None:
        """Genau der Fall, für den das hier existiert: das leere Profil.

        Ein 404 wäre absurd — wer noch nichts geschrieben hat, ist die Person,
        die Hilfe beim Anfangen braucht.
        """
        drafter = RecordingDrafter()

        response = await _post(_app(drafter, None), wish="Ich bin Pflegefachkraft.")

        assert response.status_code == 200
        assert drafter.seen is not None
        assert drafter.seen.headline == ""
        assert drafter.seen.wish == "Ich bin Pflegefachkraft."


@pytest.mark.parametrize("path", ["/profiles/me"])
async def test_reading_the_profile_never_calls_a_provider(path: str) -> None:
    """Der Aufruf nach außen darf keinen Lesepfad blockieren.

    Ein Anbieter mit 30 Sekunden Zeitlimit im Lesepfad hieße: die Profilseite
    ist so schnell wie der langsamste fremde Dienst.
    """

    class ExplodingDrafter:
        async def draft(self, context: DraftContext) -> str:
            raise AssertionError("Der Lesepfad hat einen Anbieter gerufen")

    app = _app(ExplodingDrafter(), _profile())
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        response = await client.get(path)

    assert response.status_code == 200
