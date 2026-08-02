"""Ports der Application-Schicht."""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from resume_service.domain.request import ResumeRequest
from resume_service.domain.resume import Resume

__all__ = ["ConsentGate", "ResumeRepository", "ResumeRequestRepository"]


class ResumeRepository(Protocol):
    async def get(self, subject_id: UUID) -> Resume | None: ...
    async def save(self, resume: Resume) -> None: ...


class ResumeRequestRepository(Protocol):
    async def get(self, request_id: UUID) -> ResumeRequest | None: ...
    async def find(self, subject_id: UUID, tenant_id: UUID) -> ResumeRequest | None: ...
    async def add(self, request: ResumeRequest) -> None: ...
    async def save(self, request: ResumeRequest) -> None: ...
    async def for_subject(self, subject_id: UUID) -> list[ResumeRequest]: ...
    async def for_tenant(self, tenant_id: UUID) -> list[ResumeRequest]: ...


class ConsentGate(Protocol):
    """Lesen UND schreiben — anders als beim Profil.

    Die Person erteilt und widerruft über diesen Dienst, damit der
    Capability-String an genau einer Stelle entsteht. Wirft `ConsentUnavailable`,
    wenn der Ledger schweigt; ein `False` wäre dann eine Aussage über die Person,
    die niemand treffen kann.
    """

    async def may_see_profile(self, subject_id: UUID, *, bearer: str) -> bool: ...
    async def may_read_resume(self, subject_id: UUID, tenant_id: UUID, *, bearer: str) -> bool: ...
    async def grant_resume(self, subject_id: UUID, tenant_id: UUID, *, bearer: str) -> None: ...
    async def revoke_resume(self, subject_id: UUID, tenant_id: UUID, *, bearer: str) -> None: ...
