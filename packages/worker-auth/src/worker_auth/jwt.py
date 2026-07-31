"""JWT issuance and verification with PyJWT (HS256)."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import UUID, uuid4

import jwt as pyjwt
from pydantic import BaseModel

__all__ = ["ExpiredToken", "InvalidToken", "TokenManager", "TokenPayload"]


class InvalidToken(Exception):
    """A JWT could not be verified (bad signature, malformed, wrong type)."""


class ExpiredToken(InvalidToken):
    """A JWT's exp claim is in the past."""


class TokenPayload(BaseModel):
    sub: UUID
    tenant_id: UUID
    roles: list[str] = []
    permissions: list[str] = []
    exp: int
    iat: int
    type: str  # "access" | "refresh"
    jti: str


class TokenManager:
    def __init__(
        self,
        secret: str,
        *,
        algorithm: str = "HS256",
        access_token_expire_minutes: int = 15,
        refresh_token_expire_minutes: int = 1440,
    ) -> None:
        if algorithm != "HS256":
            raise ValueError(f"Only HS256 is supported in Phase 2, got {algorithm!r}")
        self.secret = secret
        self.algorithm = algorithm
        self.access_token_expire_minutes = access_token_expire_minutes
        self.refresh_token_expire_minutes = refresh_token_expire_minutes

    def create_access_token(
        self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]
    ) -> str:
        return self._encode(
            user_id=user_id,
            tenant_id=tenant_id,
            roles=roles,
            permissions=permissions,
            token_type="access",  # noqa: S106 - JWT claim discriminator, not a secret
            expire_minutes=self.access_token_expire_minutes,
            jti=str(uuid4()),
        )

    def create_refresh_token(self, user_id: UUID, tenant_id: UUID, *, session_jti: str) -> str:
        return self._encode(
            user_id=user_id,
            tenant_id=tenant_id,
            roles=[],
            permissions=[],
            token_type="refresh",  # noqa: S106 - JWT claim discriminator, not a secret
            expire_minutes=self.refresh_token_expire_minutes,
            jti=session_jti,
        )

    def verify_token(self, token: str, *, expected_type: str) -> TokenPayload:
        try:
            claims: dict[str, Any] = pyjwt.decode(token, self.secret, algorithms=[self.algorithm])
        except pyjwt.ExpiredSignatureError as exc:
            raise ExpiredToken("Token expired") from exc
        except pyjwt.InvalidTokenError as exc:
            raise InvalidToken("Token could not be verified") from exc
        if claims.get("type") != expected_type:
            raise InvalidToken(f"Expected token type {expected_type!r}, got {claims.get('type')!r}")
        try:
            return TokenPayload(**claims)
        except Exception as exc:
            raise InvalidToken("Token claims did not match the expected schema") from exc

    def _encode(
        self,
        *,
        user_id: UUID,
        tenant_id: UUID,
        roles: list[str],
        permissions: list[str],
        token_type: str,
        expire_minutes: int,
        jti: str,
    ) -> str:
        now = datetime.now(UTC)
        payload: dict[str, Any] = {
            "sub": str(user_id),
            "tenant_id": str(tenant_id),
            "roles": roles,
            "permissions": permissions,
            "exp": int((now + timedelta(minutes=expire_minutes)).timestamp()),
            "iat": int(now.timestamp()),
            "type": token_type,
            "jti": jti,
        }
        return str(pyjwt.encode(payload, self.secret, algorithm=self.algorithm))
