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


async def test_verification_token_roundtrip_and_single_use(session: object) -> None:
    from datetime import UTC, datetime, timedelta

    from identity_service.domain.verification import TokenPurpose, VerificationToken
    from identity_service.infrastructure.database.repositories import (
        SqlAlchemyVerificationTokenRepository,
    )

    user_repo = SqlAlchemyUserRepository(session)  # type: ignore[arg-type]
    token_repo = SqlAlchemyVerificationTokenRepository(session)  # type: ignore[arg-type]
    user = User.register(
        email=Email("tok@example.com"),
        password_hash=PasswordHash("$2b$12$t"),
        display_name="Tok",
        now=datetime.now(UTC),
    )
    await user_repo.add(user)
    token = VerificationToken(
        token_id=uuid4(),
        user_id=user.id.value,
        token_hash="a" * 64,
        purpose=TokenPurpose.EMAIL_VERIFY,
        expires_at=datetime.now(UTC) + timedelta(hours=24),
        consumed_at=None,
    )
    await token_repo.add(token)
    await session.commit()  # type: ignore[attr-defined]

    found = await token_repo.get_by_hash("a" * 64)
    assert found is not None and found.is_consumed() is False

    await token_repo.consume(found.token_id, datetime.now(UTC))
    await session.commit()  # type: ignore[attr-defined]

    again = await token_repo.get_by_hash("a" * 64)
    assert again is not None and again.is_consumed() is True


async def test_company_roundtrip_by_domain(session: object) -> None:
    from identity_service.domain.company import Company, EmailDomain
    from identity_service.infrastructure.database.repositories import SqlAlchemyCompanyRepository

    repo = SqlAlchemyCompanyRepository(session)  # type: ignore[arg-type]
    company = Company.create(name="Firma GmbH", domain=EmailDomain("firma-roundtrip.de"))

    await repo.add(company)
    await session.commit()  # type: ignore[attr-defined]

    # CITEXT: die Suche ist unabhängig von der Schreibweise.
    found = await repo.get_by_domain("FIRMA-ROUNDTRIP.DE")
    assert found is not None and found.name == "Firma GmbH"


async def test_membership_add_and_list_for_user_detailed(session: object) -> None:
    from identity_service.domain.company import Company, EmailDomain
    from identity_service.domain.membership import MembershipRole
    from identity_service.infrastructure.database.repositories import (
        SqlAlchemyCompanyRepository,
        SqlAlchemyMembershipRepository,
    )

    user_repo = SqlAlchemyUserRepository(session)  # type: ignore[arg-type]
    company_repo = SqlAlchemyCompanyRepository(session)  # type: ignore[arg-type]
    membership_repo = SqlAlchemyMembershipRepository(session)  # type: ignore[arg-type]

    user = User.register(
        email=Email("member@firma-detailed.de"),
        password_hash=PasswordHash("$2b$12$m"),
        display_name="Member",
        now=datetime.now(UTC),
    )
    await user_repo.add(user)
    company = Company.create(name="Detailed GmbH", domain=EmailDomain("firma-detailed.de"))
    await company_repo.add(company)
    await session.commit()  # type: ignore[attr-defined]

    await membership_repo.add(user.id.value, company.id, MembershipRole.ADMIN)
    await session.commit()  # type: ignore[attr-defined]

    views = await membership_repo.list_for_user_detailed(user.id.value)
    assert len(views) == 1
    assert views[0].tenant_id == company.id
    assert views[0].name == "Detailed GmbH"
    assert views[0].domain == "firma-detailed.de"
    assert views[0].role is MembershipRole.ADMIN
