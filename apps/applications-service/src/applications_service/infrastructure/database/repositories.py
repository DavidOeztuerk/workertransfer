"""SQLAlchemy-Umsetzung der Bewerbungs-Ports."""

from __future__ import annotations

from uuid import UUID

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from applications_service.domain.application import (
    Application,
    ApplicationStatus,
    SharedArtifacts,
)
from applications_service.infrastructure.database.models import ApplicationModel

__all__ = ["SqlAlchemyApplicationRepository"]


def _to_domain(row: ApplicationModel) -> Application:
    return Application(
        id=row.id,
        job_id=row.job_id,
        tenant_id=row.tenant_id,
        subject_id=row.subject_id,
        message=row.message,
        shared=SharedArtifacts(resume=row.shares_resume, portfolio=row.shares_portfolio),
        status=ApplicationStatus(row.status),
        created_at=row.created_at,
        updated_at=row.updated_at,
        answered_at=row.answered_at,
    )


class SqlAlchemyApplicationRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, application_id: UUID) -> Application | None:
        row = await self._session.get(ApplicationModel, application_id)
        return None if row is None else _to_domain(row)

    async def find(self, job_id: UUID, subject_id: UUID) -> Application | None:
        stmt = select(ApplicationModel).where(
            ApplicationModel.job_id == job_id, ApplicationModel.subject_id == subject_id
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _to_domain(row)

    async def add(self, application: Application) -> None:
        self._session.add(
            ApplicationModel(
                id=application.id,
                job_id=application.job_id,
                tenant_id=application.tenant_id,
                subject_id=application.subject_id,
                message=application.message,
                shares_resume=application.shared.resume,
                shares_portfolio=application.shared.portfolio,
                status=str(application.status),
                created_at=application.created_at,
                updated_at=application.updated_at,
                answered_at=application.answered_at,
            )
        )

    async def save(self, application: Application) -> None:
        row = await self._session.get(ApplicationModel, application.id)
        if row is None:
            await self.add(application)
            return
        # Alle veränderlichen Felder schreiben: ein vergessenes kostet im Test
        # nichts und verliert in Produktion lautlos den Schreibvorgang.
        row.message = application.message
        row.shares_resume = application.shared.resume
        row.shares_portfolio = application.shared.portfolio
        row.status = str(application.status)
        row.updated_at = application.updated_at
        row.answered_at = application.answered_at

    async def for_subject(self, subject_id: UUID) -> list[Application]:
        return await self._listed(ApplicationModel.subject_id == subject_id)

    async def for_job(self, job_id: UUID) -> list[Application]:
        return await self._listed(ApplicationModel.job_id == job_id)

    async def _listed(self, condition: object) -> list[Application]:
        # Neueste zuerst: eine frische Bewerbung ist wichtiger als eine
        # entschiedene von vorletztem Monat.
        stmt = (
            select(ApplicationModel)
            .where(condition)  # type: ignore[arg-type]
            .order_by(ApplicationModel.created_at.desc())
        )
        return [_to_domain(row) for row in (await self._session.execute(stmt)).scalars()]

    async def count_by_status(self, tenant_id: UUID) -> dict[str, int]:
        """Zählt die EIGENEN Bewerbungen je Status (ADR-0026).

        `GROUP BY` in der Datenbank statt Laden und Zählen in Python: die Liste
        könnte groß werden, und sie ganz zu holen, nur um sie zu zählen, hieße
        personenbezogene Zeilen ohne Not durch den Dienst zu tragen.
        """
        rows = await self._session.execute(
            select(ApplicationModel.status, func.count())
            .where(ApplicationModel.tenant_id == tenant_id)
            .group_by(ApplicationModel.status)
        )
        return {status: count for status, count in rows.all()}
