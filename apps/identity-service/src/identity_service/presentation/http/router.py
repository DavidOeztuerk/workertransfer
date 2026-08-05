"""HTTP endpoints for authentication: /auth/{register,login,refresh,logout}."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, Cookie, HTTPException, Request, Response, status
from pydantic import BaseModel
from worker_contracts import RegisterUserV1, ResendVerificationV1, VerifyEmailV1

from identity_service.application.commands import (
    AuthenticateUserCommand,
    OutgoingMail,
    RefreshTokenCommand,
    RegisterUserCommand,
    ResendVerificationCommand,
    RevokeTokenCommand,
    SwitchTenantCommand,
    VerifyEmailCommand,
    dispatch_all,
    handle_login,
    handle_refresh,
    handle_register,
    handle_resend,
    handle_revoke,
    handle_switch_tenant,
    handle_verify_email,
)
from identity_service.application.ports import TokenPair
from identity_service.domain.membership import NotAMember
from identity_service.domain.user import (
    AccountDisabled,
    EmailNotConfirmed,
    InvalidCredentials,
)
from identity_service.domain.verification import TokenExpired
from identity_service.presentation.auth_middleware import get_request_user


class LoginBody(BaseModel):
    email: str
    password: str


def build_auth_router(deps: dict[str, Any]) -> APIRouter:
    # Die Bremse gegen Durchprobieren steht NICHT hier, sondern als Middleware
    # im Composition-Root (`AUTH_LIMITS` in `compose_api.py`). Dort, weil sie
    # weiter außen greifen muss als die Authentifizierung: sonst würde für
    # jeden Rateversuch erst bcrypt gerechnet, und die Bremse wäre der teuerste
    # Teil des Angriffs.
    router = APIRouter(prefix="/auth", tags=["auth"])
    settings = deps["settings"]
    session_factory = deps["session_factory"]
    request_scope = deps["request_scope"]

    def _secure() -> bool:
        from worker_platform.configuration import Environment

        return settings.environment is Environment.PRODUCTION

    def _set_cookies(response: Response, pair: TokenPair) -> None:
        secure = _secure()
        response.set_cookie("access", pair.access, httponly=True, samesite="strict", secure=secure)
        response.set_cookie(
            "refresh",
            pair.refresh,
            httponly=True,
            samesite="strict",
            secure=secure,
            path="/auth",
        )

    @router.post("/register", status_code=status.HTTP_201_CREATED)
    async def register(body: RegisterUserV1) -> dict[str, str]:
        cmd = RegisterUserCommand(body.email, body.password, body.display_name)
        # Mails werden gesammelt und ERST NACH dem Commit versandt: die UoW
        # committet im __aexit__ des request_scope, ein Versand innerhalb würde
        # den Bestätigungslink verschicken, bevor die Token-Zeile existiert.
        outbox: list[OutgoingMail] = []
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_register(cmd, deps=deps, repos=repos, outbox=outbox)
        if not result.is_success:
            err = result.error
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        # Auch bei bereits vergebener Adresse: identische Antwort, kein 409.
        # Ein 409 beantwortet "ist diese Person hier?" ohne den Consent-Ledger
        # zu fragen (product-scope.md) — auf einem Transfermarkt genau die
        # Information, die jemanden den Arbeitsplatz kosten kann. Der echte
        # Besitzer bekommt stattdessen eine Warnmail.
        await dispatch_all(outbox, deps)
        return {"status": "registered"}

    @router.post("/login")
    async def login(body: LoginBody, response: Response) -> dict[str, str]:
        cmd = AuthenticateUserCommand(body.email, body.password)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_login(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, EmailNotConfirmed):
                # Bei korrektem Passwort verrät das nichts, was das Passwort
                # nicht ohnehin beweist — und ohne diesen Fall wäre ein
                # unbestätigtes Konto eine Sackgasse.
                raise HTTPException(status.HTTP_403_FORBIDDEN, "email not confirmed")
            if isinstance(err, (InvalidCredentials, AccountDisabled)):
                raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        _set_cookies(response, result.value)
        return {"status": "ok"}

    @router.post("/verify-email")
    async def verify_email(body: VerifyEmailV1) -> dict[str, str]:
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_verify_email(
                VerifyEmailCommand(token=body.token), deps=deps, repos=repos
            )
        if not result.is_success:
            if isinstance(result.error, TokenExpired):
                # 410 statt 400, damit die Oberfläche gezielt "erneut senden"
                # anbieten kann. Der Token kennt ohnehin nur der Empfänger, die
                # Unterscheidung verrät also nichts.
                raise HTTPException(status.HTTP_410_GONE, "confirmation link expired")
            raise HTTPException(status.HTTP_400_BAD_REQUEST, "invalid confirmation link")
        return {"status": "ok"}

    @router.post("/resend-verification", status_code=status.HTTP_202_ACCEPTED)
    async def resend(body: ResendVerificationV1) -> dict[str, str]:
        # Immer 202 — auch bei unbekannter Adresse und bei längst bestätigtem
        # Konto. Sonst wäre dieser Endpunkt der Enumerationskanal, den
        # /auth/register gerade schließt.
        outbox: list[OutgoingMail] = []
        async with request_scope(session_factory) as (_uow, repos):
            await handle_resend(
                ResendVerificationCommand(email=body.email),
                deps=deps,
                repos=repos,
                outbox=outbox,
            )
        # Erst nach dem Commit — sonst ginge der neue Link raus, bevor die
        # Entwertung des alten committet ist.
        await dispatch_all(outbox, deps)
        return {"status": "accepted"}

    @router.post("/refresh")
    async def refresh(
        response: Response, refresh: str | None = Cookie(default=None, alias="refresh")
    ) -> dict[str, str]:
        if refresh is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
        cmd = RefreshTokenCommand(refresh_token=refresh)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_refresh(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, (InvalidCredentials, AccountDisabled)):
                raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        _set_cookies(response, result.value)
        return {"status": "ok"}

    @router.post("/company/{tenant_id}")
    async def switch_tenant(
        tenant_id: UUID, request: Request, response: Response
    ) -> dict[str, str]:
        """Act for a company from now on — if the caller is a member of it.

        The caller may ask for any tenant; membership decides. That is what keeps
        the tenant out of client control even though the client names it.
        """
        principal = get_request_user(request.scope)
        if principal is None:
            raise HTTPException(status.HTTP_401_UNAUTHORIZED, "not authenticated")
        cmd = SwitchTenantCommand(user_id=principal.user_id, tenant_id=tenant_id)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_switch_tenant(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, NotAMember):
                # 403, not 404: never reveal whether the company exists.
                raise HTTPException(status.HTTP_403_FORBIDDEN, "not a member of this tenant")
            if isinstance(err, (InvalidCredentials, AccountDisabled)):
                raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        _set_cookies(response, result.value)
        return {"status": "ok", "tenant_id": str(tenant_id)}

    @router.post("/logout", status_code=status.HTTP_204_NO_CONTENT)
    async def logout(
        refresh: str | None = Cookie(default=None, alias="refresh"),
    ) -> Response:
        # Idempotent: a missing/invalid refresh token has nothing to revoke.
        if refresh is not None:
            cmd = RevokeTokenCommand(refresh_token=refresh)
            async with request_scope(session_factory) as (_uow, repos):
                await handle_revoke(cmd, deps=deps, repos=repos)
        resp = Response(status_code=status.HTTP_204_NO_CONTENT)
        resp.delete_cookie("refresh", path="/auth")
        # Auch das Access-Cookie löschen. Der frühere Kommentar behauptete, es
        # werde "beim nächsten geschützten Request abgelehnt, sobald die Session
        # weg ist" — das prüft niemand: verify_access_token validiert nur
        # Signatur und Ablauf und sieht die sessions-Tabelle nie. Ohne das
        # Löschen blieb man nach dem Abmelden bis zu 15 Minuten angemeldet, auf
        # einem geteilten Rechner ein echtes Problem. Pfad muss zum Setzen
        # passen (dort ohne path, also "/").
        resp.delete_cookie("access", path="/")
        return resp

    return router


__all__ = ["LoginBody", "build_auth_router"]
