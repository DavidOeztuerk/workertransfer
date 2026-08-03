"""Der Zugriff auf GitHub — einmal lesen, nicht zusehen.

Es gibt hier bewusst keinen Hintergrundabgleich, keinen Nachtlauf und keinen
Webhook. ADR-0004 verbietet Scraping; der Buchstabe wäre mit einem periodischen
Abruf eingehalten, der Sinn nicht: eine Plattform, die einem Menschen dauerhaft
hinterhersieht, tut etwas anderes als eine, die einmal auf seine Bitte hinsieht.

Nebenbei löst das die 60 Anfragen pro Stunde: die Zahl der Abrufe hängt an
Handlungen von Menschen, nicht an einer Uhr.
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any

import httpx

from github_service.domain.connection import Repository, challenge_description

__all__ = ["GitHubUnavailable", "HttpGitHub"]

_logger = logging.getLogger("workertransfer.github.client")

#: Nur die erste Seite. Wer mehr als hundert öffentliche Repositories hat,
#: bekommt die hundert zuletzt geänderten — eine ehrliche Auswahl, und die
#: Anzeige sagt, wonach sortiert wurde. Alles zu holen hieße, für die seltenen
#: Fälle jedem anderen mehrere Abrufe aufzubürden.
PER_PAGE = 100


class GitHubUnavailable(RuntimeError):
    """GitHub hat nicht geantwortet — ein Systemzustand, kein fachlicher Ausgang."""


def _parse_time(raw: Any) -> datetime | None:
    if not isinstance(raw, str):
        return None
    try:
        return datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        return None


class HttpGitHub:
    def __init__(
        self,
        *,
        base_url: str = "https://api.github.com",
        token: str = "",
        transport: httpx.AsyncBaseTransport | None = None,
        timeout: float = 10.0,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._token = token
        self._transport = transport
        self._timeout = timeout

    def _headers(self) -> dict[str, str]:
        headers = {"Accept": "application/vnd.github+json"}
        # Ein Token hebt das Ratenlimit von 60 auf 5000 je Stunde. Ohne Token
        # läuft alles genauso, nur knapper — deshalb ist es optional.
        if self._token:
            headers["Authorization"] = f"Bearer {self._token}"
        return headers

    async def _get(self, path: str, params: dict[str, Any] | None = None) -> Any:
        try:
            async with httpx.AsyncClient(
                transport=self._transport, timeout=self._timeout
            ) as client:
                response = await client.get(
                    f"{self._base_url}{path}", params=params, headers=self._headers()
                )
        except httpx.HTTPError as exc:
            _logger.warning("GitHub nicht erreichbar: %s", exc)
            raise GitHubUnavailable("github unreachable") from exc

        if response.status_code == 404:
            # „Gibt es nicht" ist eine Antwort, kein Ausfall.
            return None
        if response.status_code != 200:
            _logger.warning("GitHub antwortete mit %s", response.status_code)
            raise GitHubUnavailable(f"github returned {response.status_code}")
        try:
            return response.json()
        except Exception as exc:
            raise GitHubUnavailable("github sent an unusable answer") from exc

    async def has_challenge_gist(self, login: str, challenge: str) -> bool:
        """Steht die Einmalzeichenfolge in der Beschreibung eines Gists?

        Die Beschreibung, nicht der Inhalt: die Liste liefert sie mit, ein
        Inhalt bräuchte einen Abruf je Gist.
        """
        wanted = challenge_description(challenge)
        payload = await self._get(f"/users/{login}/gists", {"per_page": PER_PAGE})
        if not isinstance(payload, list):
            return False
        return any(
            isinstance(entry, dict) and entry.get("description") == wanted for entry in payload
        )

    async def repositories(self, login: str) -> list[Repository]:
        payload = await self._get(
            f"/users/{login}/repos", {"per_page": PER_PAGE, "sort": "pushed", "type": "owner"}
        )
        if not isinstance(payload, list):
            return []
        found: list[Repository] = []
        for entry in payload:
            if not isinstance(entry, dict):
                continue
            # Forks bleiben draußen: eine Kopie fremder Arbeit ist kein Beleg
            # für eigene, und sie unter „meine Repositories" zu zeigen wäre
            # genau die stillschweigende Behauptung, die ADR-0022 ausschließt.
            if entry.get("fork") is True:
                continue
            found.append(
                Repository(
                    name=str(entry.get("name", "")),
                    description=str(entry.get("description") or ""),
                    language=entry.get("language")
                    if isinstance(entry.get("language"), str)
                    else None,
                    stars=int(entry.get("stargazers_count") or 0),
                    url=str(entry.get("html_url", "")),
                    pushed_at=_parse_time(entry.get("pushed_at")),
                )
            )
        return found
