"""SQLAlchemy-Umsetzung des Verbindungs-Ports."""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any
from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from github_service.domain.connection import GitHubConnection, Repository
from github_service.infrastructure.database.models import GitHubConnectionModel

__all__ = ["SqlAlchemyGitHubConnectionRepository"]


def _repo_to_json(repo: Repository) -> dict[str, Any]:
    return {
        "name": repo.name,
        "description": repo.description,
        "language": repo.language,
        "stars": repo.stars,
        "url": repo.url,
        "pushed_at": repo.pushed_at.isoformat() if repo.pushed_at is not None else None,
    }


def _repo_from_json(raw: dict[str, Any]) -> Repository:
    pushed = raw.get("pushed_at")
    return Repository(
        name=str(raw.get("name", "")),
        description=str(raw.get("description", "")),
        language=raw.get("language"),
        stars=int(raw.get("stars", 0)),
        url=str(raw.get("url", "")),
        pushed_at=datetime.fromisoformat(pushed) if isinstance(pushed, str) else None,
    )


class SqlAlchemyGitHubConnectionRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, subject_id: UUID) -> GitHubConnection | None:
        row = await self._session.get(GitHubConnectionModel, subject_id)
        if row is None:
            return None
        return GitHubConnection(
            subject_id=row.id,
            login=row.login,
            challenge=row.challenge,
            verified_at=row.verified_at,
            fetched_at=row.fetched_at,
            repositories=[_repo_from_json(entry) for entry in row.repositories],
        )

    async def save(self, connection: GitHubConnection) -> None:
        row = await self._session.get(GitHubConnectionModel, connection.subject_id)
        now = datetime.now(UTC)
        if row is None:
            row = GitHubConnectionModel(id=connection.subject_id, created_at=now, updated_at=now)
            self._session.add(row)
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.login = connection.login
        row.challenge = connection.challenge
        row.verified_at = connection.verified_at
        row.fetched_at = connection.fetched_at
        row.repositories = [_repo_to_json(r) for r in connection.repositories]
        row.updated_at = now

    async def delete(self, subject_id: UUID) -> None:
        row = await self._session.get(GitHubConnectionModel, subject_id)
        if row is not None:
            await self._session.delete(row)
