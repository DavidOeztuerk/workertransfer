"""Authentication: JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management."""

from __future__ import annotations

from worker_auth.jwt import ExpiredToken, InvalidToken, TokenManager, TokenPayload
from worker_auth.middleware import (
    DEFAULT_COOKIE_NAME,
    JwtAuthMiddleware,
    extract_bearer_token,
    extract_cookie_token,
    resolve_token,
)
from worker_auth.password import (
    BcryptPasswordHasher,
    PasswordHashError,
    PasswordTooLong,
    hash_password,
    verify_password,
)

__all__ = [
    "DEFAULT_COOKIE_NAME",
    "BcryptPasswordHasher",
    "ExpiredToken",
    "InvalidToken",
    "JwtAuthMiddleware",
    "PasswordHashError",
    "PasswordTooLong",
    "TokenManager",
    "TokenPayload",
    "extract_bearer_token",
    "extract_cookie_token",
    "hash_password",
    "resolve_token",
    "verify_password",
]
