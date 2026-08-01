"""HTTP endpoints for authentication: /auth/{register,login,refresh,logout}."""

from __future__ import annotations

from typing import Any
from uuid import UUID

from fastapi import APIRouter, Cookie, HTTPException, Request, Response, status
from pydantic import BaseModel

from identity_service.application.commands import (
    AuthenticateUserCommand,
    RefreshTokenCommand,
    RegisterUserCommand,
    RevokeTokenCommand,
    SwitchTenantCommand,
    handle_login,
    handle_refresh,
    handle_register,
    handle_revoke,
    handle_switch_tenant,
)
from identity_service.application.ports import TokenPair
from identity_service.domain.membership import NotAMember
from identity_service.domain.user import (
    AccountDisabled,
    InvalidCredentials,
    UserAlreadyExists,
)
from identity_service.presentation.auth_middleware import get_request_user


class RegisterBody(BaseModel):
    # No tenant_id: a tenant is a company, registering is an act of a person
    # (ADR-0017) — and a client-supplied tenant would be exactly the trust
    # problem product-scope.md forbids for headers.
    email: str
    password: str
    display_name: str


class LoginBody(BaseModel):
    email: str
    password: str


def build_auth_router(deps: dict[str, Any]) -> APIRouter:
    # TODO Phase-10: enforce per-IP rate-limiting (worker-ratelimit) on /auth/login
    # and /auth/refresh before any external exposure; the auth flow currently has
    # no brute-force throttle (rate-limiting was explicitly out of Phase-2 scope).
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
    async def register(body: RegisterBody) -> dict[str, str]:
        cmd = RegisterUserCommand(body.email, body.password, body.display_name)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_register(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, UserAlreadyExists):
                raise HTTPException(status.HTTP_409_CONFLICT, "user already exists")
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        return {"status": "registered"}

    @router.post("/login")
    async def login(body: LoginBody, response: Response) -> dict[str, str]:
        cmd = AuthenticateUserCommand(body.email, body.password)
        async with request_scope(session_factory) as (_uow, repos):
            result = await handle_login(cmd, deps=deps, repos=repos)
        if not result.is_success:
            err = result.error
            if isinstance(err, (InvalidCredentials, AccountDisabled)):
                raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid credentials")
            raise HTTPException(
                status.HTTP_400_BAD_REQUEST, err.message if err is not None else "invalid"
            )
        _set_cookies(response, result.value)
        return {"status": "ok"}

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

    @router.post("/tenant/{tenant_id}")
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
        # Do not clear the access cookie here — it is short-lived and will be
        # rejected on the next protected request once the session jti is gone.
        return resp

    return router


__all__ = ["LoginBody", "RegisterBody", "build_auth_router"]
