"""JWT adapter — bridges worker-auth TokenManager to the application ports."""

from __future__ import annotations

from uuid import UUID

from worker_auth import TokenManager

from identity_service.application.ports import AuthPrincipal, TokenPair


class JwTokenService:
    def __init__(
        self,
        secret: str,
        *,
        access_expire_minutes: int = 15,
        refresh_expire_minutes: int = 1440,
    ) -> None:
        self._manager = TokenManager(
            secret=secret,
            access_token_expire_minutes=access_expire_minutes,
            refresh_token_expire_minutes=refresh_expire_minutes,
        )

    def issue_access_token(
        self, user_id: UUID, tenant_id: UUID, roles: list[str], permissions: list[str]
    ) -> str:
        return self._manager.create_access_token(user_id, tenant_id, roles, permissions)

    def issue_refresh_token(self, user_id: UUID, tenant_id: UUID, *, session_jti: str) -> str:
        return self._manager.create_refresh_token(user_id, tenant_id, session_jti=session_jti)

    def issue_pair(
        self,
        *,
        user_id: UUID,
        tenant_id: UUID,
        roles: list[str],
        permissions: list[str],
        session_jti: str,
    ) -> TokenPair:
        access = self.issue_access_token(user_id, tenant_id, roles, permissions)
        refresh = self.issue_refresh_token(user_id, tenant_id, session_jti=session_jti)
        return TokenPair(access=access, refresh=refresh)

    def _verify(self, token: str, *, expected_type: str) -> AuthPrincipal:
        payload = self._manager.verify_token(token, expected_type=expected_type)
        return AuthPrincipal(
            user_id=payload.sub, tenant_id=payload.tenant_id, roles=tuple(payload.roles)
        )

    def verify_access_token(self, token: str) -> AuthPrincipal:
        return self._verify(token, expected_type="access")

    def verify_refresh_token(self, token: str) -> AuthPrincipal:
        return self._verify(token, expected_type="refresh")
