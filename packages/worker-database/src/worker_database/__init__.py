"""Database layer: SQLAlchemy 2, Alembic, UoW, Repository."""

from __future__ import annotations

from datetime import UTC, datetime
from types import TracebackType
from typing import Any

from sqlalchemy import DateTime
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class TimestampMixin:
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        default=lambda: datetime.now(UTC),
        onupdate=lambda: datetime.now(UTC),
        nullable=False,
    )


class SoftDeleteMixin:
    deleted_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True, index=True
    )
    is_deleted: Mapped[bool] = mapped_column(default=False, nullable=False)

    def soft_delete(self) -> None:
        self.deleted_at = datetime.now(UTC)
        self.is_deleted = True


class TenantMixin:
    pass  # tenant_id added by concrete models


class VersionMixin:
    version: Mapped[int] = mapped_column(default=1, nullable=False)

    def increment_version(self) -> None:
        self.version += 1


def create_engine(url: str, **kwargs: Any) -> AsyncEngine:
    return create_async_engine(url, pool_pre_ping=True, **kwargs)


def create_session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False, autoflush=False)


class UnitOfWork:
    def __init__(self, session_factory: async_sessionmaker[AsyncSession]):
        self._session_factory = session_factory
        self._session: AsyncSession | None = None

    @property
    def session(self) -> AsyncSession:
        if self._session is None:
            raise RuntimeError("UnitOfWork not started. Use async with.")
        return self._session

    async def __aenter__(self) -> UnitOfWork:
        self._session = self._session_factory()
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        if exc_type:
            await self.rollback()
        else:
            await self.commit()
        if self._session is not None:
            await self._session.close()
        self._session = None

    async def commit(self) -> None:
        await self.session.commit()

    async def rollback(self) -> None:
        await self.session.rollback()

    async def flush(self) -> None:
        await self.session.flush()
