"""SQLAlchemy-Umsetzung der Job-Ports."""

from __future__ import annotations

import base64
from datetime import datetime
from uuid import UUID

from sqlalchemy import or_, select
from sqlalchemy.ext.asyncio import AsyncSession

from jobs_service.domain.job import EmploymentType, Job, JobStatus, RemoteMode, Skills
from jobs_service.infrastructure.database.models import JobModel

__all__ = ["SqlAlchemyJobRepository", "decode_cursor", "encode_cursor"]


def encode_cursor(published_at: datetime, job_id: UUID) -> str:
    """Der Cursor trägt beide Sortierschlüssel.

    `published_at` allein reicht nicht: zwei Stellen, die in derselben Sekunde
    veröffentlicht wurden, würden sich beim Blättern gegenseitig überspringen
    oder doppelt erscheinen.
    """
    return base64.urlsafe_b64encode(f"{published_at.isoformat()}|{job_id}".encode()).decode()


def decode_cursor(cursor: str) -> tuple[datetime, UUID] | None:
    """Ein unlesbarer Cursor ist kein Fehler, sondern der Anfang.

    Er kommt aus einer URL und wird kopiert, gekürzt und weitergereicht; darauf
    mit einem 400 zu antworten hilft niemandem.
    """
    try:
        raw = base64.urlsafe_b64decode(cursor.encode()).decode()
        stamp, job_id = raw.split("|", 1)
        return datetime.fromisoformat(stamp), UUID(job_id)
    except Exception:
        return None


def _to_domain(row: JobModel) -> Job:
    return Job(
        id=row.id,
        tenant_id=row.tenant_id,
        title=row.title,
        description=row.description,
        location=row.location,
        remote=RemoteMode(row.remote),
        employment=EmploymentType(row.employment),
        skills=Skills(list(row.skills)),
        status=JobStatus(row.status),
        published_at=row.published_at,
        created_at=row.created_at,
        updated_at=row.updated_at,
    )


class SqlAlchemyJobRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, job_id: UUID) -> Job | None:
        row = await self._session.get(JobModel, job_id)
        return None if row is None else _to_domain(row)

    async def add(self, job: Job) -> None:
        self._session.add(
            JobModel(
                id=job.id,
                tenant_id=job.tenant_id,
                title=job.title,
                description=job.description,
                location=job.location,
                remote=str(job.remote),
                employment=str(job.employment),
                skills=list(job.skills.value),
                status=str(job.status),
                published_at=job.published_at,
                created_at=job.created_at,
                updated_at=job.updated_at,
            )
        )

    async def save(self, job: Job) -> None:
        row = await self._session.get(JobModel, job.id)
        if row is None:
            await self.add(job)
            return
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.title = job.title
        row.description = job.description
        row.location = job.location
        row.remote = str(job.remote)
        row.employment = str(job.employment)
        row.skills = list(job.skills.value)
        row.status = str(job.status)
        row.published_at = job.published_at
        row.updated_at = job.updated_at

    async def for_tenant(self, tenant_id: UUID) -> list[Job]:
        stmt = (
            select(JobModel)
            .where(JobModel.tenant_id == tenant_id)
            .order_by(JobModel.updated_at.desc(), JobModel.id.desc())
        )
        return [_to_domain(row) for row in (await self._session.execute(stmt)).scalars()]

    async def search(
        self,
        *,
        query: str | None,
        location: str | None,
        remote: str | None,
        employment: str | None,
        company: UUID | None,
        limit: int,
        cursor: str | None,
    ) -> tuple[list[Job], str | None]:
        """Nur veröffentlichte, neueste zuerst.

        Kein Relevanz-Ranking: ein Ranking ist eine Aussage darüber, was
        wichtiger ist, und die will begründet sein. Chronologisch ist ehrlich
        und nachvollziehbar.
        """
        stmt = select(JobModel).where(JobModel.status == str(JobStatus.PUBLISHED))
        if query:
            pattern = f"%{query}%"
            stmt = stmt.where(
                or_(JobModel.title.ilike(pattern), JobModel.description.ilike(pattern))
            )
        if location:
            stmt = stmt.where(JobModel.location.ilike(f"%{location}%"))
        if remote:
            stmt = stmt.where(JobModel.remote == remote)
        if employment:
            stmt = stmt.where(JobModel.employment == employment)
        if company is not None:
            # Für die Karriere-Seite: dieselbe Menge mit einer Bedingung
            # mehr. Ein eigener Endpunkt hätte einen zweiten Filter, der
            # irgendwann vom ersten abweicht.
            stmt = stmt.where(JobModel.tenant_id == company)

        position = decode_cursor(cursor) if cursor else None
        if position is not None:
            stamp, job_id = position
            stmt = stmt.where(
                or_(
                    JobModel.published_at < stamp,
                    (JobModel.published_at == stamp) & (JobModel.id < job_id),
                )
            )

        stmt = stmt.order_by(JobModel.published_at.desc(), JobModel.id.desc()).limit(limit + 1)
        rows = list((await self._session.execute(stmt)).scalars())

        # limit + 1 geholt, um ohne zweite Abfrage zu wissen, ob es weitergeht.
        next_cursor: str | None = None
        if len(rows) > limit:
            rows = rows[:limit]
            last = rows[-1]
            if last.published_at is not None:
                next_cursor = encode_cursor(last.published_at, last.id)
        return [_to_domain(row) for row in rows], next_cursor
