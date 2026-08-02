"""Ports der Application-Schicht."""

from __future__ import annotations

from typing import Protocol
from uuid import UUID

from resume_service.domain.resume import Resume

__all__ = ["ResumeRepository"]


class ResumeRepository(Protocol):
    async def get(self, subject_id: UUID) -> Resume | None: ...
    async def save(self, resume: Resume) -> None: ...
