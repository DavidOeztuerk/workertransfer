"""SQLAlchemy-Umsetzung der Lebenslauf-Ports."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from resume_service.domain.request import RequestStatus, ResumeRequest
from resume_service.domain.resume import Education, MonthDate, Position, Resume
from resume_service.infrastructure.database.models import ResumeModel, ResumeRequestModel

__all__ = ["SqlAlchemyResumeRepository", "SqlAlchemyResumeRequestRepository"]


def _month_to_json(value: MonthDate | None) -> str | None:
    return None if value is None else str(value)


def _position_to_json(entry: Position) -> dict[str, Any]:
    return {
        "employer": entry.employer,
        "title": entry.title,
        "started_on": str(entry.started_on),
        "ended_on": _month_to_json(entry.ended_on),
        "description": entry.description,
    }


def _education_to_json(entry: Education) -> dict[str, Any]:
    return {
        "institution": entry.institution,
        "qualification": entry.qualification,
        "started_on": str(entry.started_on),
        "ended_on": _month_to_json(entry.ended_on),
    }


def _position_from_json(raw: dict[str, Any]) -> Position:
    ended = raw.get("ended_on")
    return Position(
        employer=raw["employer"],
        title=raw["title"],
        started_on=MonthDate.parse(raw["started_on"]),
        ended_on=None if ended is None else MonthDate.parse(ended),
        description=raw.get("description", ""),
    )


def _education_from_json(raw: dict[str, Any]) -> Education:
    ended = raw.get("ended_on")
    return Education(
        institution=raw["institution"],
        qualification=raw.get("qualification", ""),
        started_on=MonthDate.parse(raw["started_on"]),
        ended_on=None if ended is None else MonthDate.parse(ended),
    )


def _to_domain(row: ResumeModel) -> Resume:
    # Geht durch `create`, nicht am Konstruktor vorbei: die Reihenfolge und die
    # Ein-offene-Station-Regel sollen auch für gespeicherte Zeilen gelten, damit
    # eine per Hand veränderte Zeile nicht unbemerkt durchrutscht.
    resume = Resume.create(
        row.id,
        positions=[_position_from_json(entry) for entry in row.positions],
        education=[_education_from_json(entry) for entry in row.education],
        now=row.updated_at,
    )
    resume.created_at = row.created_at
    return resume


class SqlAlchemyResumeRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, subject_id: UUID) -> Resume | None:
        row = await self._session.get(ResumeModel, subject_id)
        return None if row is None else _to_domain(row)

    async def save(self, resume: Resume) -> None:
        row = await self._session.get(ResumeModel, resume.subject_id)
        if row is None:
            row = ResumeModel(
                id=resume.subject_id,
                created_at=resume.created_at,
                updated_at=resume.updated_at,
                positions=[],
                education=[],
            )
            self._session.add(row)
        # Alle veränderlichen Felder schreiben. Ein vergessenes Feld kostet im
        # Test nichts (die Fakes geben dasselbe Objekt zurück) und verliert in
        # Produktion lautlos den Schreibvorgang.
        row.positions = [_position_to_json(entry) for entry in resume.positions]
        row.education = [_education_to_json(entry) for entry in resume.education]
        row.updated_at = resume.updated_at


def _request_to_domain(row: ResumeRequestModel) -> ResumeRequest:
    return ResumeRequest(
        id=row.id,
        subject_id=row.subject_id,
        tenant_id=row.tenant_id,
        requested_by=row.requested_by,
        status=RequestStatus(row.status),
        created_at=row.created_at,
        answered_at=row.answered_at,
    )


class SqlAlchemyResumeRequestRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get(self, request_id: UUID) -> ResumeRequest | None:
        row = await self._session.get(ResumeRequestModel, request_id)
        return None if row is None else _request_to_domain(row)

    async def find(self, subject_id: UUID, tenant_id: UUID) -> ResumeRequest | None:
        stmt = select(ResumeRequestModel).where(
            ResumeRequestModel.subject_id == subject_id,
            ResumeRequestModel.tenant_id == tenant_id,
        )
        row = (await self._session.execute(stmt)).scalar_one_or_none()
        return None if row is None else _request_to_domain(row)

    async def add(self, request: ResumeRequest) -> None:
        self._session.add(
            ResumeRequestModel(
                id=request.id,
                subject_id=request.subject_id,
                tenant_id=request.tenant_id,
                requested_by=request.requested_by,
                status=str(request.status),
                created_at=request.created_at,
                answered_at=request.answered_at,
            )
        )

    async def save(self, request: ResumeRequest) -> None:
        row = await self._session.get(ResumeRequestModel, request.id)
        if row is None:
            await self.add(request)
            return
        row.status = str(request.status)
        row.answered_at = request.answered_at

    async def for_subject(self, subject_id: UUID) -> list[ResumeRequest]:
        return await self._listed(ResumeRequestModel.subject_id == subject_id)

    async def for_tenant(self, tenant_id: UUID) -> list[ResumeRequest]:
        return await self._listed(ResumeRequestModel.tenant_id == tenant_id)

    async def _listed(self, condition: Any) -> list[ResumeRequest]:
        # Neueste zuerst: eine offene Anfrage von heute ist wichtiger als eine
        # beantwortete von vorletztem Monat.
        stmt = (
            select(ResumeRequestModel)
            .where(condition)
            .order_by(ResumeRequestModel.created_at.desc())
        )
        rows = (await self._session.execute(stmt)).scalars()
        return [_request_to_domain(row) for row in rows]
