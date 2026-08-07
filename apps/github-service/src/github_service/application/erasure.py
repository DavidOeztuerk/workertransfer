"""Was „löschen" in diesem Dienst heißt (ADR-0027 §2).

`github_connections.id` **ist** die `subject_id`. Die Zeile fällt vollständig:
`login`, `challenge` und der Abzug der Repositories als JSONB.

Der `login` ist öffentlich — jeder kann ihn auf github.com nachschlagen. Das
Personendatum ist nicht der Name, sondern die **Verknüpfung**: „dieser
Plattform-Mensch ist jener GitHub-Name". Die entsteht hier, und hier fällt sie.
"""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import delete
from sqlalchemy.ext.asyncio import AsyncSession

from github_service.infrastructure.database.models import GitHubConnectionModel

__all__ = ["erase_subject"]


async def erase_subject(session: AsyncSession, subject_id: UUID) -> int:
    """Löscht die Verbindung. Gibt zurück, was stehen blieb: nichts.

    Dieser Dienst kennt keinen Aufbewahrungsfall — es gibt nichts, was einem
    Unternehmen gehört.
    """
    await session.execute(
        delete(GitHubConnectionModel).where(GitHubConnectionModel.id == subject_id)
    )
    return 0
