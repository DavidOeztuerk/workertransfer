"""Authentication CQRS commands + handlers (run inside a UoW).

Per ADR-0003 the router (Task 18) drives the per-request UoW explicitly:

    async with request_scope(session_factory) as (uow, repos):
        result = await handle_register(cmd, deps=deps, repos=repos)

These handlers consume the wiring bundle (deps) and a per-request repos dict
bound to one AsyncSession; the router commits the UoW on success so audit +
domain state commit together (atomicity, ADR-0012).
"""

from __future__ import annotations

import secrets
from dataclasses import dataclass
from datetime import timedelta
from typing import Any
from uuid import UUID

from worker_core import DomainError, Result

from identity_service.application.ports import TokenPair
from identity_service.domain.audit import AuditAction, AuditEvent
from identity_service.domain.password_policy import PasswordPolicy
from identity_service.domain.user import (
    AccountDisabled,
    InvalidCredentials,
    User,
    UserAlreadyExists,
)
from identity_service.domain.value_objects import Email, TenantId

__all__ = [
    "AuthenticateUserCommand",
    "RefreshTokenCommand",
    "RegisterUserCommand",
    "RevokeTokenCommand",
]


def _correlation_id() -> str | None:
    # Local import: keep the application layer free of a top-level
    # worker_platform dep at import time.
    from worker_platform.context import get_correlation_id

    return get_correlation_id()


@dataclass(frozen=True, slots=True)
class RegisterUserCommand:
    email: str
    password: str
    display_name: str
    tenant_id: UUID


async def handle_register(
    cmd: RegisterUserCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[User]:
    hasher = deps["hasher"]
    policy: PasswordPolicy = PasswordPolicy()
    try:
        policy.validate(cmd.password)
        existing = await repos["users"].get_by_email(cmd.tenant_id, cmd.email)
        if existing is not None:
            # Audit the failed attempt too, but keep the 409 semantics in the router.
            raise UserAlreadyExists(cmd.email)
        user = User.register(
            email=Email(cmd.email),
            password_hash=hasher.hash(cmd.password),
            display_name=cmd.display_name,
            tenant_id=TenantId(cmd.tenant_id),
            now=deps["clock"].now(),
        )
        await repos["users"].add(user)
        await repos["audit"].append(
            AuditEvent(
                occurred_at=deps["clock"].now(),
                actor_id=user.id.value,
                tenant_id=user.tenant_id.value,
                action=AuditAction.REGISTER,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    await _publish_user_events(user, deps)
    return Result.ok(user)


@dataclass(frozen=True, slots=True)
class AuthenticateUserCommand:
    email: str
    password: str
    tenant_id: UUID


async def handle_login(
    cmd: AuthenticateUserCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[TokenPair]:
    hasher = deps["hasher"]
    tokens = deps["tokens"]
    clock = deps["clock"]
    now = clock.now()

    async def _audit_failure(reason: str, *, actor_id: UUID | None) -> None:
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=actor_id,
                tenant_id=cmd.tenant_id,
                action=AuditAction.LOGIN_FAILURE,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={"reason": reason},
            )
        )

    try:
        user = await repos["users"].get_by_email(cmd.tenant_id, cmd.email)
        if user is None:
            await _audit_failure("unknown_user", actor_id=None)
            raise InvalidCredentials()
        if not user.verify_password(cmd.password, hasher):
            await _audit_failure("bad_password", actor_id=user.id.value)
            raise InvalidCredentials()
        try:
            user.assert_can_log_in()
        except AccountDisabled:
            await _audit_failure("disabled", actor_id=user.id.value)
            raise InvalidCredentials() from None  # map to generic 401, keep reason in audit only

        jti = secrets.token_urlsafe(16)
        user.record_login(jti=jti, now=now)
        await repos["sessions"].add(
            user_id=user.id.value,
            tenant_id=user.tenant_id.value,
            refresh_jti=jti,
            expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
        )
        pair: TokenPair = tokens.issue_pair(
            user_id=user.id.value,
            tenant_id=user.tenant_id.value,
            roles=list(user.roles),
            permissions=[],
            session_jti=jti,
        )
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=user.id.value,
                tenant_id=user.tenant_id.value,
                action=AuditAction.LOGIN_SUCCESS,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    await _publish_user_events(user, deps)
    return Result.ok(pair)


@dataclass(frozen=True, slots=True)
class RefreshTokenCommand:
    refresh_token: str


async def handle_refresh(
    cmd: RefreshTokenCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[TokenPair]:
    tokens = deps["tokens"]
    clock = deps["clock"]

    try:
        # verify_refresh_token raises worker_auth.InvalidToken on bad/expired/wrong-type;
        # map to a generic InvalidCredentials so the 401 stays uniform and reveals nothing.
        principal = tokens.verify_refresh_token(cmd.refresh_token)
    except Exception:
        return Result.fail(InvalidCredentials())

    row = await repos["sessions"].get_by_jti(principal.jti)
    now = clock.now()
    if row is None or row.revoked_at is not None or row.expires_at <= now:
        return Result.fail(InvalidCredentials())

    # Rotate: revoke the old session, mint a new jti + tokens.
    await repos["sessions"].revoke(row.refresh_jti, now)
    new_jti = secrets.token_urlsafe(16)
    await repos["sessions"].add(
        user_id=principal.user_id,
        tenant_id=principal.tenant_id,
        refresh_jti=new_jti,
        expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
    )
    pair: TokenPair = tokens.issue_pair(
        user_id=principal.user_id,
        tenant_id=principal.tenant_id,
        roles=list(principal.roles),
        permissions=[],
        session_jti=new_jti,
    )
    await repos["audit"].append(
        AuditEvent(
            occurred_at=now,
            actor_id=principal.user_id,
            tenant_id=principal.tenant_id,
            action=AuditAction.TOKEN_REFRESH,
            target_id=None,
            correlation_id=_correlation_id(),
            metadata={"reason": "rotation"},
        )
    )
    return Result.ok(pair)


@dataclass(frozen=True, slots=True)
class RevokeTokenCommand:
    refresh_token: str


async def handle_revoke(
    cmd: RevokeTokenCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[None]:
    tokens = deps["tokens"]
    clock = deps["clock"]

    try:
        principal = tokens.verify_refresh_token(cmd.refresh_token)
    except Exception:
        # Idempotent revoke: a bad/expired token has nothing to revoke; report success.
        return Result.ok(None)

    row = await repos["sessions"].get_by_jti(principal.jti)
    now = clock.now()
    if row is not None and row.revoked_at is None:
        await repos["sessions"].revoke(row.refresh_jti, now)
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=principal.user_id,
                tenant_id=principal.tenant_id,
                action=AuditAction.TOKEN_REVOKE,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    return Result.ok(None)


async def _publish_user_events(user: User, deps: dict[str, Any]) -> None:
    eventbus = deps["eventbus"]
    for ev in user.pull_events():
        await eventbus.publish(ev)
