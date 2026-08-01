"""Repository roundtrip integration tests against a real Postgres container."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from identity_service.domain.audit import AuditAction, AuditEvent
from identity_service.domain.user import AccountStatus, User
from identity_service.domain.value_objects import Email, PasswordHash
from identity_service.infrastructure.database.repositories import (
    SqlAlchemyAuditRepository,
    SqlAlchemySessionRepository,
    SqlAlchemyUserRepository,
)

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")


async def test_user_repository_add_then_get_by_email(session: object) -> None:
    repo = SqlAlchemyUserRepository(session)  # type: ignore[arg-type]
    user = User.register(
        email=Email("repo@example.com"),
        password_hash=PasswordHash("$2b$12$x"),
        display_name="Repo",
        now=datetime.now(UTC),
    )
    await repo.add(user)
    await session.commit()  # type: ignore[attr-defined]

    found = await repo.get_by_email("REPO@example.com")  # CITEXT case-insensitive
    assert found is not None
    assert found.email == Email("repo@example.com")
    # Accounts start unconfirmed since Task 4; activate() is what flips this.
    assert found.status is AccountStatus.PENDING


async def test_session_audit_repositories_roundtrip(session: object) -> None:
    tenant = uuid4()
    user_repo = SqlAlchemyUserRepository(session)  # type: ignore[arg-type]
    sess_repo = SqlAlchemySessionRepository(session)  # type: ignore[arg-type]
    audit_repo = SqlAlchemyAuditRepository(session)  # type: ignore[arg-type]

    # sessions.user_id has a FK -> users.id (ON DELETE CASCADE); persist a user
    # first and reuse its id rather than a bare uuid4() that violates the FK.
    user = User.register(
        email=Email(f"{tenant}@example.com"),
        password_hash=PasswordHash("$2b$12$s"),
        display_name="Session",
        now=datetime.now(UTC),
    )
    await user_repo.add(user)

    await sess_repo.add(
        user_id=user.id.value,
        tenant_id=tenant,
        refresh_jti="jti-x",
        expires_at=datetime.now(UTC) + timedelta(days=1),
    )
    await audit_repo.append(
        AuditEvent(
            occurred_at=datetime.now(UTC),
            actor_id=user.id.value,
            tenant_id=tenant,
            action=AuditAction.LOGIN_FAILURE,
            target_id=None,
            correlation_id="c1",
            metadata={"reason": "unknown_user"},
        )
    )
    await session.commit()  # type: ignore[attr-defined]

    s = await sess_repo.get_by_jti("jti-x")
    assert s is not None and s.refresh_jti == "jti-x"
