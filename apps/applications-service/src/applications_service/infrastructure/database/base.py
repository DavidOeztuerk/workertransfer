"""Declarative base owned by applications-service.

Not `worker_database.Base`: that one shares a single MetaData across every
service, so two services owning a table with the same name collide as soon as
both model modules are imported in one process — which is exactly what pytest
does in this monorepo. It also breaks Alembic autogenerate, since a service
would see the other service's tables as missing and try to drop them.

Each service owning its own MetaData is what ADR-0004 already says about data:
no shared database, no cross-service tables. See ADR-0016.
"""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, DateTime, Integer, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

__all__ = ["Base", "SoftDeleteMixin", "TimestampMixin", "VersionMixin"]


class Base(DeclarativeBase):
    """Declarative base whose MetaData holds only applications-service tables."""


class TimestampMixin:
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, server_default=func.now()
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, server_default=func.now(), onupdate=func.now()
    )


class SoftDeleteMixin:
    is_deleted: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    deleted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)


class VersionMixin:
    version: Mapped[int] = mapped_column(Integer, nullable=False, default=1)
