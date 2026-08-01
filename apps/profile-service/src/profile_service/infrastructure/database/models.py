"""Database models for profile-service."""

from __future__ import annotations

from uuid import UUID, uuid4

from sqlalchemy import String
from sqlalchemy.orm import Mapped, mapped_column

from profile_service.infrastructure.database.base import Base, TimestampMixin

__all__ = ["ExampleModel"]


class ExampleModel(Base, TimestampMixin):
    """Platzhalter — durch die eigenen Tabellen dieses Service ersetzen.

    Bewusst minimal. Das vorherige Beispiel führte Indizes, Soft-Delete, eine
    tenant_id-Spalte und Domänen-Mapping vor — und war dabei nicht einmal
    importierbar (`postgresql_where` auf einem UniqueConstraint, dazu ein
    Lambda auf einen undefinierten Namen). Ein Beispiel, das mehr zeigt als es
    trägt, lädt außerdem dazu ein, Entscheidungen zu kopieren, die nicht
    allgemein gelten: ein Tenant ist ein Unternehmen, personenbezogene Daten
    werden nicht über ihn getrennt (ADR-0017).

    Die Mixins für Soft-Delete und Versionierung stehen in base.py bereit,
    falls dieser Service sie braucht.
    """

    __tablename__ = "examples"

    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
