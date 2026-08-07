"""Ausschreibungen gegen echtes Postgres.

Die interessante Frage ist nicht, ob Speichern funktioniert, sondern was ein
fremdes Unternehmen und was die Öffentlichkeit sieht.
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

        from jobs_service.configuration import JobsServiceSettings
        from jobs_service.presentation.compose_api import build_app

        yield build_app(JobsServiceSettings())
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
        "title": "Backend-Entwicklerin",
        "description": "Was zu tun ist.",
        "location": "Berlin",
        "remote": "hybrid",
        "employment": "full_time",
        **overrides,
    }


async def _draft(app: Any, token: str, **overrides: Any) -> dict[str, Any]:
    response = await _call(app, "POST", "/jobs", token, json=_body(**overrides))
    assert response.status_code == 201, response.text
    return dict(response.json())


async def test_a_draft_is_invisible_until_it_is_published(app: Any) -> None:
    token = _token(uuid4())
    draft = await _draft(app, token, title=f"Entwurf {uuid4().hex[:8]}")

    # Für die Öffentlichkeit gibt es ihn nicht.
    hidden = await _call(app, "GET", f"/jobs/{draft['id']}")
    assert hidden.status_code == 404

    published = await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)
    assert published.status_code == 200, published.text
    assert published.json()["published_at"] is not None

    visible = await _call(app, "GET", f"/jobs/{draft['id']}")
    assert visible.status_code == 200


async def test_the_public_search_needs_no_login(app: Any) -> None:
    """Eine Ausschreibung, die man nur angemeldet sieht, ist keine."""
    token = _token(uuid4())
    title = f"Öffentlich {uuid4().hex[:8]}"
    draft = await _draft(app, token, title=title)
    await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    # Ohne jedes Token.
    found = await _call(app, "GET", f"/jobs?q={title}")

    assert found.status_code == 200, found.text
    assert [item["title"] for item in found.json()["items"]] == [title]


async def test_a_closed_job_disappears_from_the_public_view(app: Any) -> None:
    token = _token(uuid4())
    title = f"Geschlossen {uuid4().hex[:8]}"
    draft = await _draft(app, token, title=title)
    await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    closed = await _call(app, "POST", f"/jobs/{draft['id']}/close", token)
    assert closed.status_code == 200, closed.text

    assert (await _call(app, "GET", f"/jobs/{draft['id']}")).status_code == 404
    assert (await _call(app, "GET", f"/jobs?q={title}")).json()["items"] == []


async def test_a_closed_job_cannot_be_published_again(app: Any) -> None:
    token = _token(uuid4())
    draft = await _draft(app, token)
    await _call(app, "POST", f"/jobs/{draft['id']}/close", token)

    again = await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    # 409, nicht 422: die Eingabe ist in Ordnung, der Zustand passt nicht.
    assert again.status_code == 409, again.text


async def test_a_foreign_company_sees_nothing_it_could_not_guess(app: Any) -> None:
    """Nicht vorhanden und nicht meins sind von außen dasselbe.

    Ein Unterschied wäre ein Orakel darüber, welche Unternehmen wie viele
    Stellen ausschreiben — im Wettbewerb etwas wert.
    """
    owner = _token(uuid4())
    draft = await _draft(app, owner)
    stranger = _token(uuid4())

    theirs = await _call(app, "PUT", f"/jobs/{draft['id']}", stranger, json=_body(title="Geklaut"))
    invented = await _call(app, "PUT", f"/jobs/{uuid4()}", stranger, json=_body())

    assert theirs.status_code == invented.status_code == 404
    assert theirs.json()["detail"] == invented.json()["detail"]

    # Und der Titel steht unverändert.
    still = await _call(app, "GET", "/companies/me/jobs", owner)
    assert [job["title"] for job in still.json()] == ["Backend-Entwicklerin"]


async def test_posting_without_a_company_is_refused(app: Any) -> None:
    response = await _call(app, "POST", "/jobs", _token(None), json=_body())

    # Aussage über den Aufrufer, nicht über ein fremdes Unternehmen.
    assert response.status_code == 403, response.text


async def test_a_published_job_may_still_be_corrected(app: Any) -> None:
    token = _token(uuid4())
    draft = await _draft(app, token)
    await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    fixed = await _call(
        app, "PUT", f"/jobs/{draft['id']}", token, json=_body(title="Senior Backend-Entwicklerin")
    )

    assert fixed.status_code == 200, fixed.text
    assert fixed.json()["status"] == "published"
    # Der Zeitpunkt der Veröffentlichung bleibt: die Stelle ist dieselbe.
    assert fixed.json()["published_at"] is not None


async def test_the_filters_narrow_and_the_cursor_continues(app: Any) -> None:
    token = _token(uuid4())
    marker = uuid4().hex[:8]
    for index in range(3):
        draft = await _draft(
            app,
            token,
            title=f"Suche {marker} {index}",
            remote="full" if index == 0 else "none",
        )
        await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    only_remote = await _call(app, "GET", f"/jobs?q={marker}&remote=full")
    assert len(only_remote.json()["items"]) == 1

    first = await _call(app, "GET", f"/jobs?q={marker}&limit=2")
    assert len(first.json()["items"]) == 2
    cursor = first.json()["next_cursor"]
    assert cursor is not None

    second = await _call(app, "GET", f"/jobs?q={marker}&limit=2&cursor={cursor}")
    assert len(second.json()["items"]) == 1
    # Keine Überschneidung: der Cursor trägt beide Sortierschlüssel.
    seen = {item["id"] for item in first.json()["items"]}
    assert seen.isdisjoint({item["id"] for item in second.json()["items"]})


async def test_a_broken_cursor_starts_from_the_beginning(app: Any) -> None:
    # Er kommt aus einer URL und wird kopiert und gekürzt; ein 400 hülfe
    # niemandem.
    response = await _call(app, "GET", "/jobs?cursor=nicht-base64!!")

    assert response.status_code == 200


async def test_the_company_filter_narrows_to_one_employer(app: Any) -> None:
    """Für die Karriere-Seite: dieselbe Menge mit einer Bedingung mehr.

    Ein eigener Endpunkt hätte einen zweiten Filter, der irgendwann vom ersten
    abweicht — dann zeigte die Karriere-Seite etwas anderes als die Suche.
    """
    marker = uuid4().hex[:8]
    mine, theirs = uuid4(), uuid4()
    for tenant, label in ((mine, "meine"), (theirs, "fremde")):
        draft = await _draft(app, _token(tenant), title=f"Filter {marker} {label}")
        await _call(app, "POST", f"/jobs/{draft['id']}/publish", _token(tenant))

    only_mine = await _call(app, "GET", f"/jobs?q={marker}&company={mine}")

    assert only_mine.status_code == 200, only_mine.text
    assert [item["title"] for item in only_mine.json()["items"]] == [f"Filter {marker} meine"]


async def test_the_company_filter_never_shows_drafts(app: Any) -> None:
    tenant = uuid4()
    token = _token(tenant)
    await _draft(app, token, title=f"Entwurf {uuid4().hex[:8]}")

    response = await _call(app, "GET", f"/jobs?company={tenant}")

    assert response.json()["items"] == []


async def test_the_required_skills_travel_to_the_public_view(app: Any) -> None:
    """Sie gehen ganz nach draußen — abgeglichen wird im Browser.

    Deshalb steht hier keine Suche nach Fähigkeiten und keine Kennzahl: die
    Liste ist alles, was der Server dazu beiträgt.
    """
    token = _token(uuid4())
    title = f"Fähigkeiten {uuid4().hex[:8]}"
    draft = await _draft(app, token, title=title, skills=["  Python  ", "python", "", "Kubernetes"])
    await _call(app, "POST", f"/jobs/{draft['id']}/publish", token)

    public = await _call(app, "GET", f"/jobs/{draft['id']}")

    assert public.status_code == 200, public.text
    # Getrimmt, entdoppelt ohne Rücksicht auf Groß-/Kleinschreibung, leere weg.
    assert public.json()["skills"] == ["Python", "Kubernetes"]
    assert (await _call(app, "GET", f"/jobs?q={title}")).json()["items"][0]["skills"] == [
        "Python",
        "Kubernetes",
    ]


async def test_a_job_without_skills_reports_an_empty_list_not_null(app: Any) -> None:
    """`null` würde im Browser durch jede `.length`-Prüfung fallen.

    Und es wäre die falsche Aussage: die Stelle hat nichts aufgezählt — das ist
    eine leere Liste, kein fehlender Wert.
    """
    body = _body(title=f"Ohne {uuid4().hex[:8]}")
    body.pop("skills", None)
    response = await _call(app, "POST", "/jobs", _token(uuid4()), json=body)

    assert response.status_code == 201, response.text
    assert response.json()["skills"] == []


async def test_editing_replaces_the_list_instead_of_merging_it(app: Any) -> None:
    token = _token(uuid4())
    draft = await _draft(app, token, skills=["Python", "Go"])

    fixed = await _call(
        app, "PUT", f"/jobs/{draft['id']}", token, json=_body(skills=["Python", "Kubernetes"])
    )

    assert fixed.status_code == 200, fixed.text
    # Go ist gestrichen und bleibt gestrichen.
    assert fixed.json()["skills"] == ["Python", "Kubernetes"]


async def test_too_many_skills_are_refused_as_input_not_stored_truncated(app: Any) -> None:
    response = await _call(
        app,
        "POST",
        "/jobs",
        _token(uuid4()),
        json=_body(skills=[f"skill-{index}" for index in range(21)]),
    )

    # 422: die Eingabe ist falsch. Stillschweigend zu kürzen hieße, eine Liste
    # zu veröffentlichen, die das Unternehmen so nicht geschrieben hat.
    assert response.status_code == 422, response.text


async def test_the_draft_endpoint_needs_an_active_company(app: Any) -> None:
    """Ohne die Prüfung wäre der Endpunkt ein Textgenerator für jeden Angemeldeten.

    Aussage über den Aufrufer, nicht über ein fremdes Unternehmen — deshalb 403.
    """
    response = await _call(app, "POST", "/jobs/draft", _token(None), json={"title": "X"})

    assert response.status_code == 403, response.text


async def test_without_a_provider_the_draft_is_503_and_says_nothing_else(app: Any) -> None:
    # Kein Schlüssel im Test, also `NullDrafter`: die Funktion ist ehrlich aus.
    # 503 heißt „später noch einmal", nicht „falsch gemacht".
    response = await _call(
        app,
        "POST",
        "/jobs/draft",
        _token(uuid4()),
        json={"title": "Pflegefachkraft", "description": "Text", "wish": "kürzer"},
    )

    assert response.status_code == 503, response.text
    # Und der eingesandte Text taucht in der Absage nicht auf.
    assert "Pflegefachkraft" not in response.text
