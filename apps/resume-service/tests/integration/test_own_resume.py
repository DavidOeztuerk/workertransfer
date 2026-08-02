"""Der eigene Lebenslauf gegen echtes Postgres.

Was Unit-Tests hier nicht können: prüfen, dass ein Schreibvorgang die Anfrage
überlebt. Beim Profil war genau das der Fehler, den 41 grüne Unit-Tests nicht
sehen konnten — die Fakes gaben dasselbe Objekt zurück, und ein fehlendes
`save()` fiel erst in Produktion auf.
"""

from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest
from alembic import command
from alembic.config import Config
from httpx import ASGITransport, AsyncClient
from worker_auth import TokenManager

_SERVICE_DIR = Path(__file__).resolve().parents[2]
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"

pytestmark = pytest.mark.asyncio(loop_scope="module")


@pytest.fixture(scope="module")
def app(postgres_url: str) -> Iterator[Any]:
    patch = pytest.MonkeyPatch()
    try:
        cfg = Config()
        cfg.set_main_option("script_location", str(_SERVICE_DIR / "migrations"))
        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        command.upgrade(cfg, "head")
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from resume_service.configuration import ResumeServiceSettings
        from resume_service.presentation.compose_api import build_app

        yield build_app(ResumeServiceSettings())
    finally:
        patch.undo()


def _token(user_id: UUID) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, None, ["user"], [])


def _position(**overrides: Any) -> dict[str, Any]:
    return {
        "employer": "Acme GmbH",
        "title": "Backend-Entwicklerin",
        "started_on": "2020-01",
        "ended_on": "2023-06",
        "description": "",
        **overrides,
    }


async def _put(app: Any, token: str, body: dict[str, Any]) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://resume") as client:
        return await client.put(
            "/resumes/me", json=body, headers={"Authorization": f"Bearer {token}"}
        )


async def _get(app: Any, token: str) -> Any:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://resume") as client:
        return await client.get("/resumes/me", headers={"Authorization": f"Bearer {token}"})


async def test_a_saved_resume_survives_the_request(app: Any) -> None:
    token = _token(uuid4())

    saved = await _put(app, token, {"positions": [_position()], "education": []})
    assert saved.status_code == 200, saved.text

    fetched = await _get(app, token)
    assert fetched.status_code == 200
    assert fetched.json()["positions"][0]["employer"] == "Acme GmbH"


async def test_without_a_resume_the_answer_is_null_not_a_404(app: Any) -> None:
    fetched = await _get(app, _token(uuid4()))

    assert fetched.status_code == 200
    assert fetched.json() is None


async def test_a_second_save_replaces_instead_of_appending(app: Any) -> None:
    token = _token(uuid4())
    await _put(app, token, {"positions": [_position(employer="Erste")], "education": []})

    await _put(app, token, {"positions": [_position(employer="Zweite")], "education": []})

    body = (await _get(app, token)).json()
    assert [entry["employer"] for entry in body["positions"]] == ["Zweite"]


async def test_the_stored_order_is_the_domain_order(app: Any) -> None:
    token = _token(uuid4())
    await _put(
        app,
        token,
        {
            "positions": [
                _position(employer="Alt", started_on="2015-01", ended_on="2018-01"),
                _position(employer="Jetzt", started_on="2023-02", ended_on=None),
                _position(employer="Davor", started_on="2020-01", ended_on="2023-01"),
            ],
            "education": [],
        },
    )

    body = (await _get(app, token)).json()

    # Laufende Station zuerst, dann absteigend nach Beginn — und zwar auch
    # nachdem die Zeile durch JSONB und zurück gelaufen ist.
    assert [entry["employer"] for entry in body["positions"]] == ["Jetzt", "Davor", "Alt"]


async def test_two_open_positions_are_refused_and_change_nothing(app: Any) -> None:
    token = _token(uuid4())
    await _put(app, token, {"positions": [_position(employer="Bleibt")], "education": []})

    rejected = await _put(
        app,
        token,
        {
            "positions": [
                _position(employer="Eine", ended_on=None),
                _position(employer="Zwei", ended_on=None),
            ],
            "education": [],
        },
    )

    assert rejected.status_code == 422
    body = (await _get(app, token)).json()
    assert [entry["employer"] for entry in body["positions"]] == ["Bleibt"]


async def test_a_day_precise_date_is_rejected_by_the_contract(app: Any) -> None:
    rejected = await _put(
        app,
        _token(uuid4()),
        {"positions": [_position(started_on="2020-01-14")], "education": []},
    )

    assert rejected.status_code == 422


async def test_one_persons_resume_is_not_anothers(app: Any) -> None:
    mine, yours = _token(uuid4()), _token(uuid4())
    await _put(app, mine, {"positions": [_position(employer="Meins")], "education": []})

    assert (await _get(app, yours)).json() is None
