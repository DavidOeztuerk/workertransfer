# Skill: Database Layer (SQLAlchemy 2 + Alembic + Unit of Work)

## Purpose
Implement a robust database layer with SQLAlchemy 2.0 async, Alembic migrations, Unit of Work pattern, and Repository implementations.

## Dependencies
```toml
# pyproject.toml
dependencies = [
    "sqlalchemy>=2.0.0,<3.0.0",
    "asyncpg>=0.29.0,<1.0.0",
    "alembic>=1.13.0,<2.0.0",
    "pydantic>=2.0.0,<3.0.0",
]
```

## Configuration

```python
# infrastructure/database/config.py
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession, async_sessionmaker
from sqlalchemy.pool import NullPool
from worker_config import DatabaseSettings

def create_engine(settings: DatabaseSettings) -> AsyncEngine:
    return create_async_engine(
        settings.database_url,
        echo=settings.echo_sql,
        pool_size=settings.pool_size,
        max_overflow=settings.max_overflow,
        pool_pre_ping=True,
        poolclass=NullPool if settings.environment == "test" else None,
    )

def create_session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)
```

## Base Model with Common Fields

```python
# infrastructure/database/base.py
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy import DateTime, func
from uuid import UUID, uuid4
from datetime import datetime, UTC

class Base(DeclarativeBase):
    pass

class TimestampMixin:
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=lambda: datetime.now(UTC), onupdate=lambda: datetime.now(UTC), nullable=False
    )

class SoftDeleteMixin:
    deleted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    
    @property
    def is_deleted(self) -> bool:
        return self.deleted_at is not None
    
    def soft_delete(self) -> None:
        self.deleted_at = datetime.now(UTC)
```

## Entity Models

```python
# infrastructure/database/models/user.py
from sqlalchemy import String, Index, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column, relationship
from worker_core.domain import Entity
from .base import Base, TimestampMixin, SoftDeleteMixin

class UserModel(Base, TimestampMixin, SoftDeleteMixin):
    __tablename__ = "users"
    __table_args__ = (
        Index("ix_users_email", "email", unique=True, postgresql_where=deleted_at.is_(None)),
        Index("ix_users_tenant_id", "tenant_id"),
    )
    
    id: Mapped[UUID] = mapped_column(primary_key=True, default=uuid4)
    tenant_id: Mapped[UUID] = mapped_column(nullable=False, index=True)
    email: Mapped[str] = mapped_column(String(255), nullable=False)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    hashed_password: Mapped[str] = mapped_column(String(255), nullable=False)
    is_active: Mapped[bool] = mapped_column(default=True, nullable=False)
    version: Mapped[int] = mapped_column(default=1, nullable=False)  # Optimistic locking
    
    # Relationships
    profiles: Mapped[list["ProfileModel"]] = relationship(back_populates="user", lazy="selectin")
    
    def to_domain(self) -> User:
        return User(
            id=self.id,
            tenant_id=self.tenant_id,
            email=Email(self.email),
            name=self.name,
            hashed_password=self.hashed_password,
            is_active=self.is_active,
            version=self.version,
            created_at=self.created_at,
            updated_at=self.updated_at,
        )
    
    @classmethod
    def from_domain(cls, user: User) -> "UserModel":
        return cls(
            id=user.id,
            tenant_id=user.tenant_id,
            email=user.email.value,
            name=user.name,
            hashed_password=user.hashed_password,
            is_active=user.is_active,
            version=user.version,
        )
```

## Repository Interface (Port)

```python
# application/ports/repositories.py
from typing import Protocol
from uuid import UUID
from worker_core.domain import Result

class UserRepository(Protocol):
    async def add(self, user: User) -> None: ...
    async def get(self, user_id: UUID) -> User | None: ...
    async def get_by_email(self, email: Email, tenant_id: UUID) -> User | None: ...
    async def save(self, user: User) -> Result[None, DomainError]: ...
    async def delete(self, user_id: UUID) -> None: ...
    async def list(self, spec: Specification[User], pagination: Pagination) -> list[User]: ...
```

## Repository Implementation (Adapter)

```python
# infrastructure/database/repositories/user_repo.py
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

class SqlAlchemyUserRepository(UserRepository):
    def __init__(self, session: AsyncSession):
        self._session = session
    
    async def add(self, user: User) -> None:
        self._session.add(UserModel.from_domain(user))
    
    async def get(self, user_id: UUID) -> User | None:
        stmt = select(UserModel).where(
            UserModel.id == user_id,
            UserModel.deleted_at.is_(None)
        ).options(selectinload(UserModel.profiles))
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        return model.to_domain() if model else None
    
    async def get_by_email(self, email: Email, tenant_id: UUID) -> User | None:
        stmt = select(UserModel).where(
            UserModel.email == email.value,
            UserModel.tenant_id == tenant_id,
            UserModel.deleted_at.is_(None)
        )
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        return model.to_domain() if model else None
    
    async def save(self, user: User) -> Result[None, DomainError]:
        # Optimistic locking
        stmt = select(UserModel).where(UserModel.id == user.id).with_for_update()
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        
        if model is None:
            return Result.fail(DomainError("user.not_found", "User not found"))
        
        if model.version != user.version:
            return Result.fail(DomainError("user.concurrent_modification", "User was modified by another process"))
        
        model.email = user.email.value
        model.name = user.name
        model.is_active = user.is_active
        model.version += 1
        return Result.ok(None)
    
    async def list(self, spec: Specification[User], pagination: Pagination) -> list[User]:
        stmt = spec.to_sqlalchemy(select(UserModel)).where(UserModel.deleted_at.is_(None))
        stmt = stmt.offset(pagination.offset).limit(pagination.limit)
        result = await self._session.execute(stmt)
        return [m.to_domain() for m in result.scalars()]
```

## Unit of Work

```python
# infrastructure/database/uow.py
from sqlalchemy.ext.asyncio import AsyncSession
from contextlib import asynccontextmanager

class UnitOfWork:
    def __init__(self, session_factory: async_sessionmaker[AsyncSession]):
        self._session_factory = session_factory
        self._session: AsyncSession | None = None
        self._committed = False
    
    @property
    def session(self) -> AsyncSession:
        if self._session is None:
            raise RuntimeError("UnitOfWork not started")
        return self._session
    
    async def __aenter__(self) -> "UnitOfWork":
        self._session = self._session_factory()
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb) -> None:
        if exc_type is not None:
            await self.rollback()
        elif not self._committed:
            await self.rollback()
        await self._session.close()
    
    async def commit(self) -> None:
        if self._session is None:
            raise RuntimeError("UnitOfWork not started")
        await self._session.commit()
        self._committed = True
    
    async def rollback(self) -> None:
        if self._session is not None:
            await self._session.rollback()
    
    async def flush(self) -> None:
        if self._session is not None:
            await self._session.flush()

# Repository access through UoW
class UnitOfWork:
    # ... above ...
    
    @property
    def users(self) -> UserRepository:
        return SqlAlchemyUserRepository(self.session)
    
    @property
    def profiles(self) -> ProfileRepository:
        return SqlAlchemyProfileRepository(self.session)
```

## Alembic Configuration

```ini
# alembic.ini
[alembic]
script_location = %(here)s/alembic
prepend_sys_path = .
version_path_separator = os
file_template = %%(year)d%%(month).2d%%(day).2d_%%(hour).2d%%(minute).2d_%%(rev)s_%%(slug)s

[post_write_hooks]
hooks = format
format.type = console_scripts
format.entrypoint = ruff format
format.options = --check
```

```python
# alembic/env.py
from logging.config import fileConfig
from sqlalchemy import pool
from sqlalchemy.ext.asyncio import async_engine_from_config
from alembic import context
import sys
import os

sys.path.append(os.getcwd())

from worker_database.config import create_engine, Base
from worker_config import get_database_settings

config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

target_metadata = Base.metadata

def run_migrations_offline() -> None:
    url = config.get_main_option("sqlalchemy.url")
    context.configure(url=url, target_metadata=target_metadata, literal_binds=True, dialect_opts={"paramstyle": "named"})
    with context.begin_transaction():
        context.run_migrations()

async def run_migrations_online() -> None:
    settings = get_database_settings()
    connectable = create_engine(settings)
    async with connectable.connect() as connection:
        await connection.run_sync(do_run_migrations)

def do_run_migrations(connection):
    context.configure(connection=connection, target_metadata=target_metadata, compare_type=True, compare_server_default=True)
    with context.begin_transaction():
        context.run_migrations()

if context.is_offline_mode():
    run_migrations_offline()
else:
    import asyncio
    asyncio.run(run_migrations_online())
```

## Migration Commands

```bash
# Generate migration
uv run alembic revision --autogenerate -m "add user table"

# Run migrations
uv run alembic upgrade head

# Downgrade
uv run alembic downgrade -1

# Show history
uv run alembic history

# Show current revision
uv run alembic current
```

## Multi-tenancy Support

```python
# Infrastructure tenant resolution
class TenantAwareRepository(UserRepository):
    def __init__(self, session: AsyncSession, tenant_id: UUID):
        self._session = session
        self._tenant_id = tenant_id
    
    async def get(self, user_id: UUID) -> User | None:
        stmt = select(UserModel).where(
            UserModel.id == user_id,
            UserModel.tenant_id == self._tenant_id,
            UserModel.deleted_at.is_(None)
        )
        # ...
    
    async def list(self, spec: Specification[User], pagination: Pagination) -> list[User]:
        base_spec = spec & TenantSpecification(self._tenant_id)
        # ...
```

## Testing with Testcontainers

```python
# tests/conftest.py
import pytest
from testcontainers.postgres import PostgresContainer
from sqlalchemy.ext.asyncio import create_async_engine, async_sessionmaker

@pytest.fixture(scope="session")
def postgres_container():
    container = PostgresContainer("postgres:16")
    container.start()
    yield container
    container.stop()

@pytest.fixture(scope="session")
def engine(postgres_container):
    url = postgres_container.get_connection_url().replace("psycopg2", "asyncpg")
    return create_async_engine(url)

@pytest.fixture
async def session(engine):
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    
    async_session = async_sessionmaker(engine, expire_on_commit=False)
    async with async_session() as session:
        yield session
    
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
```