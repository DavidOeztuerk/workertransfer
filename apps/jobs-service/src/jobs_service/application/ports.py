"""Ports der Application-Schicht."""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from jobs_service.domain.job import Job

__all__ = ["JobRepository"]


class JobRepository(Protocol):
    async def get(self, job_id: UUID) -> Job | None: ...
    async def add(self, job: Job) -> None: ...
    async def save(self, job: Job) -> None: ...
    async def for_tenant(self, tenant_id: UUID) -> list[Job]: ...
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
    ) -> tuple[list[Job], str | None]: ...
