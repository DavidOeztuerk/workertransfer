"""SQLAlchemy-Modelle für github-service."""

from __future__ import annotations

from datetime import datetime
from typing import Any
from uuid import UUID

from sqlalchemy import DateTime, String
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from github_service.infrastructure.database.base import Base

__all__ = ["GitHubConnectionModel"]


class GitHubConnectionModel(Base):
    """Eine Verbindung je Person; `id` IST die subject_id.

    Der Abzug liegt als JSONB daneben, nicht in einer eigenen Tabelle: er wird
    immer als Ganzes geschrieben und als Ganzes gelesen, nie einzeln abgefragt.
    Eine zweite Tabelle wäre ein Join für einen Wert, der nur zusammen Sinn
    ergibt.
    """

    __tablename__ = "github_connections"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    login: Mapped[str] = mapped_column(String(39), nullable=False)
    challenge: Mapped[str] = mapped_column(String(64), nullable=False)
    verified_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    fetched_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    repositories: Mapped[list[dict[str, Any]]] = mapped_column(JSONB, nullable=False, default=list)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
