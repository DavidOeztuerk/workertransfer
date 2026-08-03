"""Der GitHub-Zugriff gegen einen Transport im Prozess — echtes httpx, kein Netz."""

from __future__ import annotations

import httpx
import pytest
from github_service.domain.connection import challenge_description
from github_service.infrastructure.github import GitHubUnavailable, HttpGitHub


def _client(handler) -> HttpGitHub:
    return HttpGitHub(base_url="https://api.github.com", transport=httpx.MockTransport(handler))


async def test_it_finds_the_challenge_in_a_gist_description() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/users/anna/gists"
        return httpx.Response(200, json=[{"description": challenge_description("abc")}])

    assert await _client(handler).has_challenge_gist("anna", "abc") is True


async def test_a_different_challenge_does_not_count() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=[{"description": challenge_description("etwas-anderes")}])

    assert await _client(handler).has_challenge_gist("anna", "abc") is False


async def test_an_unknown_user_is_an_answer_not_an_outage() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(404, json={"message": "Not Found"})

    assert await _client(handler).has_challenge_gist("gibtesnicht", "abc") is False


async def test_a_rate_limit_is_an_outage_not_a_no() -> None:
    """403 heißt „gerade nicht", nicht „der Nachweis fehlt".

    Das zu verwechseln hieße, jemandem den Nachweis abzusprechen, weil wir zu
    oft gefragt haben.
    """

    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(403, json={"message": "API rate limit exceeded"})

    with pytest.raises(GitHubUnavailable):
        await _client(handler).has_challenge_gist("anna", "abc")


async def test_forks_are_left_out() -> None:
    # Eine Kopie fremder Arbeit ist kein Beleg für eigene.
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json=[
                {
                    "name": "eigenes",
                    "fork": False,
                    "html_url": "u",
                    "pushed_at": "2026-08-01T10:00:00Z",
                },
                {"name": "kopiert", "fork": True, "html_url": "u"},
            ],
        )

    repos = await _client(handler).repositories("anna")
    assert [r.name for r in repos] == ["eigenes"]


async def test_missing_fields_do_not_crash_the_snapshot() -> None:
    """GitHub lässt `description` und `language` leer — das ist normal."""

    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=[{"name": "leer", "html_url": "u"}])

    repo = (await _client(handler).repositories("anna"))[0]
    assert (repo.description, repo.language, repo.stars, repo.pushed_at) == ("", None, 0, None)


async def test_it_asks_only_for_owned_repositories() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen.update(dict(request.url.params))
        return httpx.Response(200, json=[])

    await _client(handler).repositories("anna")
    assert seen["type"] == "owner"


async def test_no_token_means_no_authorization_header() -> None:
    # Ohne Token läuft alles genauso, nur mit knapperem Ratenlimit.
    def handler(request: httpx.Request) -> httpx.Response:
        assert "Authorization" not in request.headers
        return httpx.Response(200, json=[])

    await _client(handler).repositories("anna")
