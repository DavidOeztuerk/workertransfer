"""Authentication CQRS commands + handlers (run inside a UoW).

Per ADR-0003 the router (Task 18) drives the per-request UoW explicitly:

    async with request_scope(session_factory) as (uow, repos):
        result = await handle_register(cmd, deps=deps, repos=repos)

These handlers consume the wiring bundle (deps) and a per-request repos dict
bound to one AsyncSession; the router commits the UoW on success so audit +
domain state commit together (atomicity, ADR-0012).
"""

from __future__ import annotations

import logging
import secrets
from dataclasses import dataclass
from datetime import timedelta
from typing import Any
from uuid import UUID, uuid4

from worker_core import DomainError, Result

from identity_service.application.ports import TokenPair
from identity_service.domain.audit import AuditAction, AuditEvent
from identity_service.domain.company import (
    AccountNotConfirmed,
    Company,
    DomainAlreadyClaimed,
    EmailDomain,
)
from identity_service.domain.membership import MembershipRole, MembershipView, NotAMember
from identity_service.domain.password_policy import PasswordPolicy
from identity_service.domain.user import (
    AccountDisabled,
    AccountStatus,
    InvalidCredentials,
    User,
)
from identity_service.domain.value_objects import Email
from identity_service.domain.verification import (
    TokenExpired,
    TokenInvalid,
    TokenPurpose,
    VerificationToken,
)
from identity_service.infrastructure.tokens import generate_token, hash_token

__all__ = [
    "AuthenticateUserCommand",
    "CreateCompanyCommand",
    "ListMembershipsQuery",
    "OutgoingMail",
    "RefreshTokenCommand",
    "RegisterUserCommand",
    "ResendVerificationCommand",
    "RevokeTokenCommand",
    "SwitchTenantCommand",
    "VerifyEmailCommand",
    "dispatch_all",
    "handle_create_company",
    "handle_list_memberships",
    "handle_register",
    "handle_resend",
    "handle_switch_tenant",
    "handle_verify_email",
]

_logger = logging.getLogger("workertransfer.identity.commands")


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


async def handle_register(
    cmd: RegisterUserCommand,
    *,
    deps: dict[str, Any],
    repos: dict[str, Any],
    outbox: list[OutgoingMail],
) -> Result[User | None]:
    hasher = deps["hasher"]
    policy: PasswordPolicy = PasswordPolicy()
    now = deps["clock"].now()
    try:
        policy.validate(cmd.password)
        # Immer hashen, auch wenn die Adresse längst vergeben ist. bcrypt mit 12
        # Runden braucht ~300 ms; ein früher Ausstieg wäre in ~10 ms zurück und
        # würde über die Antwortzeit verraten, was der gleiche Statuscode gerade
        # verbirgt. Der Enumerationskanal wandert sonst nur in die Uhr.
        password_hash = hasher.hash(cmd.password)
        existing = await repos["users"].get_by_email(cmd.email)
        if existing is not None:
            # Kein zweites Konto — aber dieselbe Antwort wie im Normalfall, damit
            # der Endpunkt nicht verrät, wer hier ein Konto hat. Der echte Besitzer
            # erfährt von dem Versuch.
            outbox.append(_duplicate_notice(existing))
            return Result.ok(None)
        user = User.register(
            email=Email(cmd.email),
            password_hash=password_hash,
            display_name=cmd.display_name,
            now=now,
        )
        await repos["users"].add(user)
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=user.id.value,
                # Registering is an act of a person, not of a company (ADR-0017).
                tenant_id=None,
                action=AuditAction.REGISTER,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
        raw_token, token_hash = generate_token()
        await repos["tokens"].add(
            VerificationToken(
                token_id=uuid4(),
                user_id=user.id.value,
                token_hash=token_hash,
                purpose=TokenPurpose.EMAIL_VERIFY,
                expires_at=now + timedelta(hours=24),
                consumed_at=None,
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    await _publish_user_events(user, deps)
    # Nur einreihen. Versandt wird nach dem Commit (dispatch_all im Router):
    # sonst ginge der Bestätigungslink raus, bevor die Token-Zeile committet
    # ist — ein fehlgeschlagener Commit ließe die Person mit einem Link auf
    # nichts zurück.
    outbox.append(
        OutgoingMail(
            to=user.email.value,
            subject="Bitte bestätige deine E-Mail-Adresse",
            body=_confirmation_body(deps, raw_token),
            user_ref=str(user.id.value),
        )
    )
    return Result.ok(user)


@dataclass(frozen=True, slots=True)
class OutgoingMail:
    """Ein Versandauftrag, der die Transaktion überleben soll.

    Handler sammeln, der Router versendet — NACH dem Commit. `user_ref` dient
    dem Log, damit ein Fehlschlag nicht die Empfängeradresse in die Logs
    schreibt (AUDIT_METADATA_ALLOWLIST schließt E-Mails genau deshalb aus).
    """

    to: str
    subject: str
    body: str
    user_ref: str


async def dispatch_all(mails: list[OutgoingMail], deps: dict[str, Any]) -> None:
    """Versendet nach dem Commit. Ein Fehlschlag darf nichts rückgängig machen —
    der Reparaturweg ist "erneut senden" (Spec §5)."""
    mailer = deps["mailer"]
    for mail in mails:
        try:
            await mailer.send(to=mail.to, subject=mail.subject, body=mail.body)
        except Exception:
            _logger.exception("Failed to send mail for user %s", mail.user_ref)


def _duplicate_notice(existing: User) -> OutgoingMail:
    """Warnt den echten Besitzer statt dem Anfragenden irgendetwas zu verraten."""
    return OutgoingMail(
        to=existing.email.value,
        subject="Registrierungsversuch mit deiner E-Mail-Adresse",
        body=(
            "Jemand hat versucht, mit deiner E-Mail-Adresse ein neues Konto bei "
            "WorkerTransfer anzulegen. Du hast bereits ein Konto — falls du das "
            "warst, melde dich einfach an. War es nicht du, kannst du diese "
            "Nachricht ignorieren.\n"
        ),
        user_ref=str(existing.id.value),
    )


def _confirmation_body(deps: dict[str, Any], raw_token: str) -> str:
    base = deps["settings"].public_web_url.rstrip("/")
    return (
        "Willkommen bei WorkerTransfer! Bitte bestätige deine E-Mail-Adresse "
        f"über folgenden Link:\n\n{base}/verify?token={raw_token}\n"
    )


@dataclass(frozen=True, slots=True)
class VerifyEmailCommand:
    token: str


async def handle_verify_email(
    cmd: VerifyEmailCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[None]:
    clock = deps["clock"]
    now = clock.now()
    try:
        record = await repos["tokens"].get_by_hash(hash_token(cmd.token))
        if record is None or record.purpose is not TokenPurpose.EMAIL_VERIFY:
            # Ein unbekannter Token verrät nichts über seinen Grund — sonst
            # würde der Endpunkt zum Orakel.
            raise TokenInvalid()
        user = await repos["users"].get_by_id(record.user_id)
        if user is None:
            raise TokenInvalid()
        if record.is_consumed():
            # Zweimal auf denselben Link zu klicken ist kein Fehler: das Konto
            # ist genau so freigeschaltet, wie der Klick es wollte. Wer den
            # Token hat, hatte ohnehin die Mail — hier wird nichts verraten.
            if user.status is AccountStatus.ACTIVE:
                return Result.ok(None)
            # Noch PENDING heißt: der Token wurde durch ein erneutes Senden
            # entwertet. Der alte Link darf dann nicht mehr freischalten.
            raise TokenInvalid()
        if record.is_expired(now):
            raise TokenExpired()
        user.activate(now=now)
        # Ohne save() bliebe die Freischaltung im Arbeitsspeicher: das Aggregat
        # kommt losgelöst aus dem Repository.
        await repos["users"].save(user)
        await repos["tokens"].consume(record.token_id, now)
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=user.id.value,
                tenant_id=None,
                action=AuditAction.EMAIL_VERIFIED,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    await _publish_user_events(user, deps)
    return Result.ok(None)


@dataclass(frozen=True, slots=True)
class ResendVerificationCommand:
    email: str


async def handle_resend(
    cmd: ResendVerificationCommand,
    *,
    deps: dict[str, Any],
    repos: dict[str, Any],
    outbox: list[OutgoingMail],
) -> Result[None]:
    clock = deps["clock"]
    now = clock.now()
    user = await repos["users"].get_by_email(cmd.email)
    if user is None or user.status is not AccountStatus.PENDING:
        # Dieselbe Antwort wie im Normalfall — kein Enumerationskanal, und ein
        # bereits bestätigtes Konto braucht keinen neuen Link.
        return Result.ok(None)

    # Sonst blieben beliebig viele gültige Links gleichzeitig in Umlauf, und der
    # älteste — womöglich fehlgeleitete — funktionierte weiter (Spec §4.3).
    await repos["tokens"].consume_open_for(user.id.value, TokenPurpose.EMAIL_VERIFY, now)
    raw_token, token_hash = generate_token()
    await repos["tokens"].add(
        VerificationToken(
            token_id=uuid4(),
            user_id=user.id.value,
            token_hash=token_hash,
            purpose=TokenPurpose.EMAIL_VERIFY,
            expires_at=now + timedelta(hours=24),
            consumed_at=None,
        )
    )
    outbox.append(
        OutgoingMail(
            to=user.email.value,
            subject="Bitte bestätige deine E-Mail-Adresse",
            body=_confirmation_body(deps, raw_token),
            user_ref=str(user.id.value),
        )
    )
    return Result.ok(None)


@dataclass(frozen=True, slots=True)
class AuthenticateUserCommand:
    email: str
    password: str


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
                tenant_id=None,
                action=AuditAction.LOGIN_FAILURE,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={"reason": reason},
            )
        )

    try:
        user = await repos["users"].get_by_email(cmd.email)
        if user is None:
            # Genauso teuer wie ein echter Vergleich: ohne das wäre eine
            # unbekannte Adresse an der Antwortzeit erkennbar (bcrypt ~300 ms
            # gegen ~10 ms), und der gleiche 401 würde nichts mehr verbergen.
            hasher.hash(cmd.password)
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
        # Logging in makes you yourself, never a company. Acting for a company is
        # a second, explicit step (POST /auth/tenant/{id}) that verifies
        # membership before it puts a tenant in the token — ADR-0017.
        await repos["sessions"].add(
            user_id=user.id.value,
            tenant_id=None,
            refresh_jti=jti,
            expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
        )
        pair: TokenPair = tokens.issue_pair(
            user_id=user.id.value,
            tenant_id=None,
            roles=list(user.roles),
            permissions=[],
            session_jti=jti,
        )
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=user.id.value,
                tenant_id=None,
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


@dataclass(frozen=True, slots=True)
class SwitchTenantCommand:
    user_id: UUID
    tenant_id: UUID


async def handle_switch_tenant(
    cmd: SwitchTenantCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[TokenPair]:
    """Mint a token pair that acts for a company, after verifying membership.

    The client names the company it wants; the server decides whether it may.
    That is what keeps ``product-scope.md`` intact — the tenant in the token was
    never taken from the request, it was derived from a checked membership.
    """
    tokens = deps["tokens"]
    clock = deps["clock"]
    now = clock.now()

    if not await repos["memberships"].is_member(cmd.user_id, cmd.tenant_id):
        await repos["audit"].append(
            AuditEvent(
                occurred_at=now,
                actor_id=cmd.user_id,
                # The tenant the caller *asked* for is the point of the record,
                # even though — especially though — it was refused.
                tenant_id=cmd.tenant_id,
                action=AuditAction.TENANT_SWITCH_DENIED,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
        return Result.fail(NotAMember())

    user = await repos["users"].get_by_id(cmd.user_id)
    if user is None:
        return Result.fail(InvalidCredentials())
    try:
        user.assert_can_log_in()
    except DomainError as exc:
        # DomainError, nicht nur AccountDisabled: seit der E-Mail-Bestätigung
        # wirft assert_can_log_in auch EmailNotConfirmed, und ein enger except
        # hätte den unbehandelt durchschlagen lassen statt ihn in ein Result zu
        # verwandeln. Heute unerreichbar (ohne Login kein Token), morgen nicht.
        return Result.fail(exc)

    # A fresh session rather than an edit of the old one: the old refresh token
    # keeps working as a person, and the tenant-bound one can be revoked alone.
    jti = secrets.token_urlsafe(16)
    await repos["sessions"].add(
        user_id=cmd.user_id,
        tenant_id=cmd.tenant_id,
        refresh_jti=jti,
        expires_at=now + timedelta(minutes=deps["settings"].jwt_refresh_token_expire_minutes),
    )
    pair: TokenPair = tokens.issue_pair(
        user_id=cmd.user_id,
        tenant_id=cmd.tenant_id,
        roles=list(user.roles),
        permissions=[],
        session_jti=jti,
    )
    await repos["audit"].append(
        AuditEvent(
            occurred_at=now,
            actor_id=cmd.user_id,
            tenant_id=cmd.tenant_id,
            action=AuditAction.TENANT_SWITCH,
            target_id=None,
            correlation_id=_correlation_id(),
            metadata={},
        )
    )
    return Result.ok(pair)


@dataclass(frozen=True, slots=True)
class CreateCompanyCommand:
    user_id: UUID
    name: str


async def handle_create_company(
    cmd: CreateCompanyCommand, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[Company]:
    """Legt ein Unternehmen an, dessen Domain bereits bewiesen ist.

    Die Domain stammt aus der bestätigten Adresse des Erstellers — sie steht
    nicht im Request und kann daher nicht gefälscht werden (ADR-0017/0018).
    Weil sie vor dem Anlegen bewiesen ist, gibt es keinen unverifizierten
    Unternehmenszustand, den später jeder Lesepfad mitprüfen müsste.
    """
    try:
        user = await repos["users"].get_by_id(cmd.user_id)
        if user is None:
            raise InvalidCredentials()
        if user.status is not AccountStatus.ACTIVE:
            # Eine unbestätigte Adresse beweist keine Domain.
            raise AccountNotConfirmed()

        domain = EmailDomain.from_email(user.email)
        if await repos["companies"].get_by_domain(domain.value) is not None:
            raise DomainAlreadyClaimed(domain.value)

        # Company.create prüft den Namen und lehnt Freemail-Domains ab.
        company = Company.create(name=cmd.name, domain=domain)
        await repos["companies"].add(company)
        await repos["memberships"].add(
            user_id=cmd.user_id, tenant_id=company.id, role=MembershipRole.ADMIN
        )
        await repos["audit"].append(
            AuditEvent(
                occurred_at=deps["clock"].now(),
                actor_id=cmd.user_id,
                # Anders als bei persönlichen Handlungen trägt diese Zeile einen
                # Tenant: sie betrifft ein Unternehmen (ADR-0017).
                tenant_id=company.id,
                action=AuditAction.COMPANY_CREATED,
                target_id=None,
                correlation_id=_correlation_id(),
                metadata={},
            )
        )
    except DomainError as exc:
        return Result.fail(exc)
    return Result.ok(company)


@dataclass(frozen=True, slots=True)
class ListMembershipsQuery:
    user_id: UUID


async def handle_list_memberships(
    query: ListMembershipsQuery, *, deps: dict[str, Any], repos: dict[str, Any]
) -> Result[list[MembershipView]]:
    rows = await repos["memberships"].list_for_user_detailed(query.user_id)
    return Result.ok(rows)


async def _publish_user_events(user: User, deps: dict[str, Any]) -> None:
    eventbus = deps["eventbus"]
    for ev in user.pull_events():
        await eventbus.publish(ev)
