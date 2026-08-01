"""Declarative base owned by consent-service.

Not `worker_database.Base`: that one shares a single MetaData across every
service, so two services owning a table called `audit_events` collide as soon as
both model modules are imported in one process — which is exactly what pytest
does in this monorepo. It also breaks Alembic autogenerate, since a service would
see the other service's tables as missing and try to drop them.

Each service owning its own MetaData is what ADR-0004 already says about data:
no shared database, no cross-service tables.
"""

from __future__ import annotations

from sqlalchemy.orm import DeclarativeBase

__all__ = ["Base"]


class Base(DeclarativeBase):
    """Declarative base whose MetaData holds only consent-service tables."""
