"""Arbeitgeberprofile gegen echtes Postgres."""

from __future__ import annotations

from collections.abc import Iterator
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import httpx
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

        from companies_service.configuration import CompaniesServiceSettings
        from companies_service.presentation.compose_api import build_app

        yield build_app(CompaniesServiceSettings())
    finally:
        patch.undo()


def _token(tenant_id: UUID | None) -> str:
    return TokenManager(secret=SECRET).create_access_token(uuid4(), tenant_id, ["user"], [])


async def _call(
    app: Any, method: str, path: str, token: str | None = None, **kwargs: Any
) -> httpx.Response:
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(method, path, headers=headers, **kwargs)


def _body(**overrides: Any) -> dict[str, Any]:
    return {
        "display_name": "Muster",
        "about": "Wer wir sind.",
        "website": "https://muster.example",
        "locations": ["Berlin"],
        "benefits": ["Homeoffice"],
        **overrides,
    }


async def test_a_profile_is_public_once_it_exists(app: Any) -> None:
    tenant = uuid4()
    token = _token(tenant)

    # Vorher gibt es für die Öffentlichkeit nichts — die Stelle bleibt anonym.
    assert (await _call(app, "GET", f"/companies/{tenant}/profile")).status_code == 404

    saved = await _call(app, "PUT", "/companies/me/profile", token, json=_body())
    assert saved.status_code == 200, saved.text

    # Ohne jedes Token.
    public = await _call(app, "GET", f"/companies/{tenant}/profile")
    assert public.status_code == 200, public.text
    assert public.json()["display_name"] == "Muster"


async def test_the_own_profile_is_null_before_it_exists(app: Any) -> None:
    # Beim eigenen ist „noch keins" ein Formular, kein Fehler.
    response = await _call(app, "GET", "/companies/me/profile", _token(uuid4()))

    assert response.status_code == 200
    assert response.json() is None


async def test_editing_without_a_company_is_refused(app: Any) -> None:
    response = await _call(app, "PUT", "/companies/me/profile", _token(None), json=_body())

    assert response.status_code == 403, response.text


async def test_one_company_cannot_write_anothers_profile(app: Any) -> None:
    """Der Tenant kommt aus dem Token, nicht aus dem Pfad oder dem Körper."""
    owner = uuid4()
    await _call(app, "PUT", "/companies/me/profile", _token(owner), json=_body())

    stranger_tenant = uuid4()
    await _call(
        app,
        "PUT",
        "/companies/me/profile",
        _token(stranger_tenant),
        json=_body(display_name="Geklaut"),
    )

    # Jeder hat sein eigenes; nichts wurde überschrieben.
    assert (await _call(app, "GET", f"/companies/{owner}/profile")).json()[
        "display_name"
    ] == "Muster"
    assert (await _call(app, "GET", f"/companies/{stranger_tenant}/profile")).json()[
        "display_name"
    ] == "Geklaut"


async def test_a_hostile_link_is_refused(app: Any) -> None:
    response = await _call(
        app,
        "PUT",
        "/companies/me/profile",
        _token(uuid4()),
        json=_body(website="javascript:alert(1)"),
    )

    assert response.status_code == 422, response.text


async def test_a_second_save_replaces_and_keeps_the_creation_time(app: Any) -> None:
    tenant = uuid4()
    token = _token(tenant)
    await _call(app, "PUT", "/companies/me/profile", token, json=_body())

    await _call(app, "PUT", "/companies/me/profile", token, json=_body(display_name="Muster AG"))

    public = await _call(app, "GET", f"/companies/{tenant}/profile")
    assert public.json()["display_name"] == "Muster AG"
    assert public.json()["locations"] == ["Berlin"]
