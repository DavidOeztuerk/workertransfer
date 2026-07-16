# Skill: Database Layer (SQLAlchemy 2, Alembic, UoW, Repository)

## Purpose
Implement production-ready database layer with async SQLAlchemy 2, Alembic migrations, Unit of Work, Repository pattern, multi-tenancy, and outbox pattern.

## Dependencies
```toml
# worker-database/pyproject.toml
dependencies = [
    "sqlalchemy>=2.0.0,<3.0.0",
    "asyncpg>=0.29.0,<1.0.0",
    "alembic>=1.13.0,<2.0.0",
    "pydantic>=2.8.0,<3.0.0",
]
```

## Base Models & Mixins

```python
# worker_database/models/base.py
from datetime import datetime, UTC
from uuid import UUID, uuid4
from sqlalchemy import DateTime, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, declared_attr

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
    deleted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True, index=True)
    is_deleted: Mapped[bool] = mapped_column(default=False, nullable=False)
    
    def soft_delete(self) -> None:
        self.deleted_at = datetime.now(UTC)
        self.is_deleted = True

class TenantMixin:
    @declared_attr.directive
    def tenant_id(cls) -> Mapped[UUID]:
        return mapped_column(nullable=False, index=True)

class AuditableMixin:
    created_by: Mapped[UUID | None] = mapped_column(nullable=True)
    updated_by: Mapped[UUID | None] = mapped_column(nullable=True)

class VersionMixin:
    version: Mapped[int] = mapped_column(default=1, nullable=False)
    
    def increment_version(self) -> None:
        self.version += 1
```

## Database Configuration & Session Management

```python
# worker_database/config.py
from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.pool import NullPool
from pydantic import PostgresDsn
from pydantic_settings import BaseSettings

class DatabaseSettings(BaseSettings):
    url: PostgresDsn
    pool_size: int = 20
    max_overflow: int = 10
    pool_timeout: int = 30
    pool_recycle: int = 3600
    echo: bool = False
    echo_pool: bool = False

def create_engine(settings: DatabaseSettings) -> AsyncEngine:
    return create_async_engine(
        str(settings.url),
        pool_size=settings.pool_size,
        max_overflow=settings.max_overflow,
        pool_timeout=settings.pool_timeout,
        pool_recycle=settings.pool_recycle,
        echo=settings.echo,
        echo_pool=settings.echo_pool,
        poolclass=NullPool if settings.pool_size == 0 else None,
    )

def create_session_factory(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False, autoflush=False)
```

## Unit of Work

```python
# worker_database/uow.py
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from sqlalchemy.ext.asyncio import AsyncSession
from typing import Protocol

class UnitOfWork(Protocol):
    session: AsyncSession
    
    async def __aenter__(self) -> "UnitOfWork": ...
    async def __aexit__(self, *args) -> None: ...
    async def commit(self) -> None: ...
    async def rollback(self) -> None: ...

class SqlAlchemyUnitOfWork:
    def __init__(self, session_factory: async_sessionmaker[AsyncSession]):
        self._session_factory = session_factory
        self._session: AsyncSession | None = None
    
    @property
    def session(self) -> AsyncSession:
        if self._session is None:
            raise RuntimeError("UnitOfWork not started. Use async with.")
        return self._session
    
    async def __aenter__(self) -> "SqlAlchemyUnitOfWork":
        self._session = self._session_factory()
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb) -> None:
        if exc_type:
            await self.rollback()
        else:
            await self.commit()
        await self._session.close()
        self._session = None
    
    async def commit(self) -> None:
        await self._session.commit()
    
    async def rollback(self) -> None:
        await self._session.rollback()
    
    @asynccontextmanager
    async def transaction(self) -> AsyncIterator[AsyncSession]:
        async with self._session.begin():
            yield self._session
```

## Repository Base

```python
# worker_database/repositories/base.py
from abc import ABC, abstractmethod
from typing import TypeVar, Generic, Sequence
from uuid import UUID
from sqlalchemy import select, delete, func
from sqlalchemy.ext.asyncio import AsyncSession

T = TypeVar("T")
TModel = TypeVar("TModel")
TDomain = TypeVar("TDomain")

class Repository(Generic[T], ABC):
    @abstractmethod
    async def add(self, entity: T) -> None: ...
    
    @abstractmethod
    async def get(self, id: UUID) -> T | None: ...
    
    @abstractmethod
    async def save(self, entity: T) -> None: ...
    
    @abstractmethod
    async def delete(self, entity: T) -> None: ...
    
    @abstractmethod
    async def list(self, *, limit: int = 100, offset: int = 0) -> Sequence[T]: ...

class SqlAlchemyRepository(Repository[TDomain], Generic[TDomain, TModel]):
    def __init__(self, session: AsyncSession, model_class: type[TModel], domain_mapper: "DomainMapper[TDomain, TModel]"):
        self._session = session
        self._model_class = model_class
        self._mapper = domain_mapper
    
    async def add(self, entity: TDomain) -> None:
        model = self._mapper.to_model(entity)
        self._session.add(model)
        await self._session.flush()
    
    async def get(self, id: UUID) -> TDomain | None:
        stmt = select(self._model_class).where(
            self._model_class.id == id,
            self._model_class.is_deleted == False  # noqa: E712
        )
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        return self._mapper.to_domain(model) if model else None
    
    async def save(self, entity: TDomain) -> None:
        model = self._mapper.to_model(entity)
        await self._session.merge(model)
        await self._session.flush()
    
    async def delete(self, entity: TDomain) -> None:
        await self._session.execute(
            delete(self._model_class).where(self._model_class.id == entity.id)
        )
    
    async def list(self, *, limit: int = 100, offset: int = 0) -> Sequence[TDomain]:
        stmt = select(self._model_class).where(
            self._model_class.is_deleted == False  # noqa: E712
        ).limit(limit).offset(offset)
        result = await self._session.execute(stmt)
        return [self._mapper.to_domain(m) for m in result.scalars().all()]

class DomainMapper(Generic[TDomain, TModel], ABC):
    @abstractmethod
    def to_domain(self, model: TModel) -> TDomain: ...
    @abstractmethod
    def to_model(self, domain: TDomain) -> TModel: ...
```

## Multi-Tenant Repository

```python
# worker_database/repositories/tenant.py
from contextvars import ContextVar

_tenant_id: ContextVar[UUID | None] = ContextVar("tenant_id", default=None)

def set_tenant_id(tenant_id: UUID | None) -> None:
    _tenant_id.set(tenant_id)

def get_tenant_id() -> UUID | None:
    return _tenant_id.get()

class TenantAwareRepository(SqlAlchemyRepository[TDomain, TModel]):
    async def get(self, id: UUID) -> TDomain | None:
        tenant_id = get_tenant_id()
        if tenant_id is None:
            raise RuntimeError("Tenant context not set")
        
        stmt = select(self._model_class).where(
            self._model_class.id == id,
            self._model_class.tenant_id == tenant_id,
            self._model_class.is_deleted == False  # noqa: E712
        )
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        return self._mapper.to_domain(model) if model else None
    
    async def list(self, *, limit: int = 100, offset: int = 0) -> Sequence[TDomain]:
        tenant_id = get_tenant_id()
        if tenant_id is None:
            raise RuntimeError("Tenant context not set")
        
        stmt = select(self._model_class).where(
            self._model_class.tenant_id == tenant_id,
            self._model_class.is_deleted == False  # noqa: E712
        ).limit(limit).offset(offset)
        result = await self._session.execute(stmt)
        return [self._mapper.to_domain(m) for m in result.scalars().all()]
```

## Outbox Pattern (Transactional Messaging)

```python
# worker_database/outbox.py
from dataclasses import dataclass
from datetime import datetime, UTC
from uuid import UUID, uuid4
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

@dataclass
class OutboxMessage:
    id: UUID
    event_type: str
    payload: dict
    created_at: datetime
    processed_at: datetime | None = None
    retry_count: int = 0

class OutboxRepository:
    def __init__(self, session: AsyncSession):
        self._session = session
    
    async def add(self, event_type: str, payload: dict) -> OutboxMessage:
        message = OutboxMessage(
            id=uuid4(),
            event_type=event_type,
            payload=payload,
            created_at=datetime.now(UTC),
        )
        # Store in outbox table
        self._session.add(OutboxModel(**message.__dict__))
        await self._session.flush()
        return message
    
    async def get_unprocessed(self, limit: int = 100) -> list[OutboxMessage]:
        stmt = select(OutboxModel).where(
            OutboxModel.processed_at.is_(None),
            OutboxModel.retry_count < 5
        ).order_by(OutboxModel.created_at).limit(limit)
        result = await self._session.execute(stmt)
        return [OutboxMessage(**m.__dict__) for m in result.scalars().all()]
    
    async def mark_processed(self, message_id: UUID) -> None:
        await self._session.execute(
            update(OutboxModel)
            .where(OutboxModel.id == message_id)
            .values(processed_at=datetime.now(UTC))
        )
    
    async def increment_retry(self, message_id: UUID) -> None:
        await self._session.execute(
            update(OutboxModel)
            .where(OutboxModel.id == message_id)
            .values(retry_count=OutboxModel.retry_count + 1)
        )

# In UnitOfWork - publish domain events to outbox
class SqlAlchemyUnitOfWork:
    # ... existing code ...
    
    def __init__(self, session_factory, event_publisher: "EventPublisher"):
        self._session_factory = session_factory
        self._event_publisher = event_publisher
        self._domain_events: list[DomainEvent] = []
    
    def add_domain_event(self, event: DomainEvent) -> None:
        self._domain_events.append(event)
    
    async def commit(self) -> None:
        # Save domain events to outbox
        outbox = OutboxRepository(self._session)
        for event in self._domain_events:
            await outbox.add(event.__class__.__name__, event.to_dict())
        
        await self._session.commit()
        
        # Publish after commit (async)
        for event in self._domain_events:
            await self._event_publisher.publish(event)
        
        self._domain_events.clear()
```

## Alembic Migrations

```python
# alembic/env.py
from logging.config import fileConfig
from sqlalchemy import pool
from sqlalchemy.engine import Connection
from sqlalchemy.ext.asyncio import async_engine_from_config
from alembic import context
from worker_database.models.base import Base

config = context.config

if config.config_file_name is not None:
    fileConfig(config.config_file_name)

target_metadata = Base.metadata

def run_migrations_offline() -> None:
    url = config.get_main_option("sqlalchemy.url")
    context.configure(url=url, target_metadata=target_metadata, literal_binds=True, dialect_opts={"paramstyle": "named"})
    with context.begin_transaction():
        context.run_migrations()

def do_run_migrations(connection: Connection) -> None:
    context.configure(connection=connection, target_metadata=target_metadata)
    with context.begin_transaction():
        context.run_migrations()

async def run_async_migrations() -> None:
    connectable = async_engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )
    async with connectable.connect() as connection:
        await connection.run_sync(do_run_migrations)
    await connectable.dispose()

if context.is_offline_mode():
    run_migrations_offline()
else:
    import asyncio
    asyncio.run(run_async_migrations())
```

```ini
# alembic.ini
[alembic]
script_location = worker_database:migrations
prepend_sys_path = .
version_path_separator = os
file_template = %%(year)d%%(month).2d%%(day).2d_%%(hour).2d%%(minute).2d_%%(rev).4s_%%(slug)s
```

## Example Migration

```python
# migrations/versions/20240115_120000_create_users.py
from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision = "20240115_120000"
down_revision = None

def upgrade() -> None:
    op.create_table(
        "users",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True),
        sa.Column("tenant_id", postgresql.UUID(as_uuid=True), nullable=False, index=True),
        sa.Column("email", sa.String(255), nullable=False, unique=True),
        sa.Column("password_hash", sa.String(255), nullable=False),
        sa.Column("first_name", sa.String(100), nullable=False),
        sa.Column("last_name", sa.String(100), nullable=False),
        sa.Column("is_active", sa.Boolean(), default=True, nullable=False),
        sa.Column("is_verified", sa.Boolean(), default=False, nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), onupdate=sa.func.now(), nullable=False),
        sa.Column("deleted_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("is_deleted", sa.Boolean(), default=False, nullable=False),
        sa.Column("version", sa.Integer(), default=1, nullable=False),
        sa.Column("created_by", postgresql.UUID(as_uuid=True), nullable=True),
        sa.Column("updated_by", postgresql.UUID(as_uuid=True), nullable=True),
        schema="identity"
    )
    op.create_index("ix_users_tenant_email", "users", ["tenant_id", "email"], unique=True, schema="identity")

def downgrade() -> None:
    op.drop_table("users", schema="identity")
```

## Usage in Service

```python
# identity_service/infrastructure/repositories/user_repo.py
class SqlAlchemyUserRepository(TenantAwareRepository[User, UserModel]):
    def __init__(self, session: AsyncSession):
        super().__init__(session, UserModel, UserDomainMapper())
    
    async def get_by_email(self, email: str) -> User | None:
        tenant_id = get_tenant_id()
        stmt = select(UserModel).where(
            UserModel.tenant_id == tenant_id,
            UserModel.email == email,
            UserModel.is_deleted == False  # noqa: E712
        )
        result = await self._session.execute(stmt)
        model = result.scalar_one_or_none()
        return self._mapper.to_domain(model) if model else None

class UserDomainMapper(DomainMapper[User, UserModel]):
    def to_domain(self, model: UserModel) -> User:
        return User(
            id=model.id,
            tenant_id=model.tenant_id,
            email=Email(model.email),
            password_hash=model.password_hash,
            first_name=model.first_name,
            last_name=model.last_name,
            is_active=model.is_active,
            is_verified=model.is_verified,
            created_at=model.created_at,
            updated_at=model.updated_at,
            version=model.version,
        )
    
    def to_model(self, domain: User) -> UserModel:
        return UserModel(
            id=domain.id,
            tenant_id=domain.tenant_id,
            email=domain.email.value,
            password_hash=domain.password_hash,
            first_name=domain.first_name,
            last_name=domain.last_name,
            is_active=domain.is_active,
            is_verified=domain.is_verified,
            version=domain.version,
        )
```