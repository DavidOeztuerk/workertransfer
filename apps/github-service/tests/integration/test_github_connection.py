"""Der ganze Weg an echten Diensten: nennen, beweisen, zeigen, widerrufen.

Der Consent-Ledger läuft als echte ASGI-App mit eigener Datenbank. GitHub selbst
antwortet über einen httpx-Transport im Prozess — mit echten Statuscodes, echtem
JSON und dem echten Client. Nur das Netz fehlt.

Der tragende Test ist `test_nothing_is_visible_without_the_proof`: ein fremder
Benutzername allein zeigt nichts. Ohne diese Regel könnte jemand die Arbeit
eines anderen unter sein Profil hängen.
"""

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
from sqlalchemy import create_engine, text
from worker_auth import TokenManager

_GITHUB_DIR = Path(__file__).resolve().parents[2]
_CONSENT_DIR = _GITHUB_DIR.parent / "consent-service"
_CONSENT_DB = "consent_for_github_test"
SECRET = "test-secret-with-at-least-thirty-two-bytes-xx"
CAPABILITY = "github.visibility:public"

pytestmark = pytest.mark.asyncio(loop_scope="module")


class FakeGitHub:
    """GitHub, wie es sich für diesen Test verhält.

    Kein httpx-Fake auf Client-Ebene: der echte `HttpGitHub` hat eigene Tests
    gegen einen MockTransport. Hier geht es um den Weg DURCH den Dienst, und
    dafür ist ein Port-Doppel die klarere Naht.
    """

    def __init__(self) -> None:
        self.gists: set[tuple[str, str]] = set()
        self.repos: dict[str, list[Any]] = {}
        self.down = False
        self.calls = 0

    async def has_challenge_gist(self, login: str, challenge: str) -> bool:
        self.calls += 1
        if self.down:
            from github_service.infrastructure.github import GitHubUnavailable

            raise GitHubUnavailable("boom")
        return (login, challenge) in self.gists

    async def repositories(self, login: str) -> list[Any]:
        self.calls += 1
        if self.down:
            from github_service.infrastructure.github import GitHubUnavailable

            raise GitHubUnavailable("boom")
        return list(self.repos.get(login, []))


GITHUB = FakeGitHub()


def _sync(url: str) -> str:
    return url.replace("postgresql+asyncpg://", "postgresql+psycopg://")


def _sibling(url: str, name: str) -> str:
    return url.rsplit("/", 1)[0] + "/" + name


def _migrate(service_dir: Path, url: str, patch: pytest.MonkeyPatch) -> None:
    cfg = Config()
    cfg.set_main_option("script_location", str(service_dir / "migrations"))
    patch.setenv("WORKER_DATABASE_URL", url)
    command.upgrade(cfg, "head")


def _drop_database(admin_url: str, name: str) -> None:
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        conn.execute(
            text(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                "WHERE datname = :name AND pid <> pg_backend_pid()"
            ),
            {"name": name},
        )
        conn.execute(text(f'DROP DATABASE IF EXISTS "{name}"'))
    admin.dispose()


def _truncate_all(url: str) -> None:
    engine = create_engine(_sync(url), isolation_level="AUTOCOMMIT")
    with engine.connect() as conn:
        names = [
            row[0]
            for row in conn.execute(
                text(
                    "SELECT tablename FROM pg_tables WHERE schemaname = 'public' "
                    "AND tablename <> 'alembic_version'"
                )
            )
        ]
        if names:
            conn.execute(
                text("TRUNCATE " + ", ".join(f'"{n}"' for n in names) + " RESTART IDENTITY CASCADE")
            )
    engine.dispose()


@pytest.fixture(scope="module")
def stack(postgres_url: str) -> Iterator[tuple[Any, Any]]:
    admin_url = _sync(postgres_url)
    consent_url = _sibling(postgres_url, _CONSENT_DB)
    _drop_database(admin_url, _CONSENT_DB)
    admin = create_engine(admin_url, isolation_level="AUTOCOMMIT")
    with admin.connect() as conn:
        conn.execute(text(f'CREATE DATABASE "{_CONSENT_DB}"'))
    admin.dispose()

    patch = pytest.MonkeyPatch()
    try:
        _migrate(_CONSENT_DIR, consent_url, patch)
        _migrate(_GITHUB_DIR, postgres_url, patch)
        patch.setenv("WORKER_JWT_SECRET", SECRET)

        from consent_service.configuration import ConsentServiceSettings
        from consent_service.presentation.compose_api import build_app as build_consent

        patch.setenv("WORKER_DATABASE_URL", consent_url)
        consent_app = build_consent(ConsentServiceSettings())

        import github_service.infrastructure.compose as compose_module
        from github_service.infrastructure.consent import HttpConsentGate

        original = HttpConsentGate

        def _in_process_gate(*, base_url: str) -> HttpConsentGate:
            return original(base_url=base_url, transport=ASGITransport(app=consent_app))

        patch.setattr(compose_module, "HttpConsentGate", _in_process_gate)
        patch.setattr(compose_module, "HttpGitHub", lambda **_: GITHUB)

        from github_service.configuration import GithubServiceSettings
        from github_service.presentation.compose_api import build_app as build_github

        patch.setenv("WORKER_DATABASE_URL", postgres_url)
        github_app = build_github(GithubServiceSettings())
        yield github_app, consent_app
    finally:
        patch.undo()
        _drop_database(admin_url, _CONSENT_DB)


@pytest.fixture
def apps(stack: tuple[Any, Any], postgres_url: str) -> tuple[Any, Any]:
    _truncate_all(postgres_url)
    _truncate_all(_sibling(postgres_url, _CONSENT_DB))
    GITHUB.gists.clear()
    GITHUB.repos.clear()
    GITHUB.down = False
    GITHUB.calls = 0
    return stack


def _token(user_id: UUID) -> str:
    return TokenManager(secret=SECRET).create_access_token(user_id, None, ["user"], [])


async def _call(app: Any, method: str, path: str, token: str, **kwargs: Any) -> httpx.Response:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        return await client.request(
            method, path, headers={"Authorization": f"Bearer {token}"}, **kwargs
        )


async def _release(consent_app: Any, token: str, subject: UUID, *, grant: bool) -> None:
    path = "/consent/grant" if grant else "/consent/revoke"
    body: dict[str, Any] = {"subject_id": str(subject), "capability": CAPABILITY}
    if not grant:
        body["reason"] = "moechte die Verbindung nicht mehr zeigen"
    response = await _call(consent_app, "POST", path, token, json=body)
    assert response.status_code == 200, response.text


def _repo(name: str) -> Any:
    from github_service.domain.connection import Repository

    return Repository(
        name=name, description="", language="Python", stars=1, url="u", pushed_at=None
    )


async def test_the_whole_way(apps: tuple[Any, Any]) -> None:
    """Nennen → beweisen → freigeben → sichtbar."""
    app, consent_app = apps
    person = uuid4()
    token = _token(person)

    named = await _call(app, "POST", "/github/me", token, json={"login": "anna"})
    assert named.status_code == 200, named.text
    assert named.json()["verified"] is False
    # Die Einmalzeichenfolge steht nur in der eigenen Ansicht.
    description = named.json()["challenge_description"]
    assert description.startswith("workertransfer-verify-")

    GITHUB.gists.add(("anna", description.removeprefix("workertransfer-verify-")))
    GITHUB.repos["anna"] = [_repo("etwas")]

    proven = await _call(app, "POST", "/github/me/verify", token)
    assert proven.status_code == 200, proven.text
    assert proven.json()["verified"] is True
    assert [r["name"] for r in proven.json()["repositories"]] == ["etwas"]
    assert proven.json()["challenge_description"] is None

    company = _token(uuid4())
    assert (await _call(app, "GET", f"/github/{person}", company)).status_code == 404

    await _release(consent_app, token, person, grant=True)
    seen = await _call(app, "GET", f"/github/{person}", company)
    assert seen.status_code == 200
    assert [r["name"] for r in seen.json()["repositories"]] == ["etwas"]


async def test_nothing_is_visible_without_the_proof(apps: tuple[Any, Any]) -> None:
    """Ein fremder Benutzername allein zeigt nichts.

    Ohne diese Regel könnte jemand die Arbeit eines anderen unter sein Profil
    hängen — und das Opfer erführe es nie.
    """
    app, consent_app = apps
    person = uuid4()
    token = _token(person)
    await _call(app, "POST", "/github/me", token, json={"login": "jemand-anders"})
    await _release(consent_app, token, person, grant=True)

    company = _token(uuid4())
    assert (await _call(app, "GET", f"/github/{person}", company)).status_code == 404


async def test_a_missing_gist_is_refused_and_nothing_is_fetched(apps: tuple[Any, Any]) -> None:
    app, _consent = apps
    token = _token(uuid4())
    await _call(app, "POST", "/github/me", token, json={"login": "anna"})
    GITHUB.repos["anna"] = [_repo("etwas")]

    refused = await _call(app, "POST", "/github/me/verify", token)

    assert refused.status_code == 422
    mine = await _call(app, "GET", "/github/me", token)
    assert mine.json()["verified"] is False
    assert mine.json()["repositories"] == []


async def test_github_being_down_is_503_not_a_denied_proof(apps: tuple[Any, Any]) -> None:
    """Sonst spräche man jemandem den Nachweis ab, weil WIR nicht fragen konnten."""
    app, _consent = apps
    token = _token(uuid4())
    await _call(app, "POST", "/github/me", token, json={"login": "anna"})
    GITHUB.down = True

    answer = await _call(app, "POST", "/github/me/verify", token)

    assert answer.status_code == 503


async def test_a_withdrawal_takes_effect_on_the_very_next_read(apps: tuple[Any, Any]) -> None:
    """ADR-0013 ohne Cache — und der Abzug bleibt liegen, wird nur nicht gezeigt."""
    app, consent_app = apps
    person = uuid4()
    token = _token(person)
    await _call(app, "POST", "/github/me", token, json={"login": "anna"})
    mine = await _call(app, "GET", "/github/me", token)
    GITHUB.gists.add(
        ("anna", mine.json()["challenge_description"].removeprefix("workertransfer-verify-"))
    )
    GITHUB.repos["anna"] = [_repo("etwas")]
    await _call(app, "POST", "/github/me/verify", token)
    await _release(consent_app, token, person, grant=True)

    company = _token(uuid4())
    assert (await _call(app, "GET", f"/github/{person}", company)).status_code == 200

    await _release(consent_app, token, person, grant=False)

    assert (await _call(app, "GET", f"/github/{person}", company)).status_code == 404
    # Gespeichert ist nicht gezeigt: die Person sieht ihren Abzug weiterhin.
    assert [
        r["name"] for r in (await _call(app, "GET", "/github/me", token)).json()["repositories"]
    ] == ["etwas"]


async def test_pointing_at_another_account_drops_the_proof(apps: tuple[Any, Any]) -> None:
    app, consent_app = apps
    person = uuid4()
    token = _token(person)
    await _call(app, "POST", "/github/me", token, json={"login": "anna"})
    mine = await _call(app, "GET", "/github/me", token)
    GITHUB.gists.add(
        ("anna", mine.json()["challenge_description"].removeprefix("workertransfer-verify-"))
    )
    GITHUB.repos["anna"] = [_repo("etwas")]
    await _call(app, "POST", "/github/me/verify", token)
    await _release(consent_app, token, person, grant=True)

    await _call(app, "POST", "/github/me", token, json={"login": "fremdes-konto"})

    company = _token(uuid4())
    assert (await _call(app, "GET", f"/github/{person}", company)).status_code == 404


async def test_disconnecting_deletes_the_snapshot(apps: tuple[Any, Any]) -> None:
    app, _consent = apps
    token = _token(uuid4())
    await _call(app, "POST", "/github/me", token, json={"login": "anna"})

    assert (await _call(app, "DELETE", "/github/me", token)).status_code == 204
    assert (await _call(app, "GET", "/github/me", token)).json() is None


async def test_naming_a_login_never_asks_github(apps: tuple[Any, Any]) -> None:
    """Ein Abruf verriete nur, dass jemand nach diesem Konto gefragt hat."""
    app, _consent = apps
    await _call(app, "POST", "/github/me", _token(uuid4()), json={"login": "anna"})

    assert GITHUB.calls == 0


async def test_without_a_token_there_is_nothing(apps: tuple[Any, Any]) -> None:
    app, _consent = apps
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://svc") as client:
        assert (await client.get("/github/me")).status_code == 401
