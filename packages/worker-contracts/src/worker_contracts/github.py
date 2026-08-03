"""Versionierte Boundary-DTOs für github-service (ADR-0004 §1).

Was hier fehlt, ist die Aussage: **keine Punktzahl, kein Rang, keine
abgeleitete Eigenschaft** (ADR-0022). Ein Repository ist ein Beleg mit einem
Link, und die Bewertung überlässt dieses System dem Menschen, der ihn anklickt.
"""

from __future__ import annotations

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field

__all__ = ["ConnectGitHubV1", "GitHubConnectionV1", "RepositoryV1"]


class ConnectGitHubV1(BaseModel):
    login: str = Field(..., min_length=1, max_length=39)


class RepositoryV1(BaseModel):
    name: str
    description: str
    #: Was GitHub als Hauptsprache meldet — weitergegeben, nicht ausgewertet.
    language: str | None
    stars: int
    url: str
    pushed_at: datetime | None


class GitHubConnectionV1(BaseModel):
    subject_id: UUID
    login: str
    verified: bool
    #: Steht nur in der eigenen Ansicht: die Einmalzeichenfolge nützt nur der
    #: Person, die den Gist anlegt.
    challenge_description: str | None = None
    #: Wann der Abzug geholt wurde. Die Anzeige sagt „Stand: …" — es wird nicht
    #: zugesehen, sondern einmal gelesen.
    fetched_at: datetime | None = None
    repositories: list[RepositoryV1] = Field(default_factory=list)
