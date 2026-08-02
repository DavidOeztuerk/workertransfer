"""SQLAlchemy-Modelle für portfolio-service."""

from __future__ import annotations

from datetime import datetime
from typing import Any
from uuid import UUID

from sqlalchemy import DateTime
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.dialects.postgresql import UUID as PG_UUID
from sqlalchemy.orm import Mapped, mapped_column

from portfolio_service.infrastructure.database.base import Base

__all__ = ["PortfolioModel"]


class PortfolioModel(Base):
    """Ein Portfolio je Person.

    Einträge als JSONB, nicht als Kindtabelle: sie werden nur als Ganzes gelesen
    und geschrieben, es gibt keine Abfrage über einzelne Einträge, und die
    Reihenfolge ist die der Person — eine Kindtabelle bräuchte dafür eine
    Sortierspalte, die bei jeder Bearbeitung mitgepflegt werden müsste.
    """

    __tablename__ = "portfolios"

    id: Mapped[UUID] = mapped_column(PG_UUID(as_uuid=True), primary_key=True)
    items: Mapped[list[dict[str, Any]]] = mapped_column(JSONB, nullable=False, default=list)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
