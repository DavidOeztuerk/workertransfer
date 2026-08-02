"""Authentication: JWT, OAuth2, OIDC, API Keys, Refresh tokens, Session management."""

from __future__ import annotations

from worker_auth.jwt import ExpiredToken, InvalidToken, TokenManager, TokenPayload
from worker_auth.middleware import (
    DEFAULT_COOKIE_NAME,
    DEFAULT_STATE_KEY,
    JwtAuthMiddleware,
    extract_bearer_token,
    extract_cookie_token,
    get_request_user,
    resolve_token,
)
from worker_auth.password import (
    BcryptPasswordHasher,
    PasswordHashError,
    PasswordTooLong,
    hash_password,
    verify_password,
)
from worker_auth.secrets import (
    DEV_JWT_SECRET,
    MIN_JWT_SECRET_LENGTH,
    InsecureJwtSecret,
    assert_deployable_jwt_secret,
)

__all__ = [
    "DEFAULT_COOKIE_NAME",
    "DEFAULT_STATE_KEY",
    "DEV_JWT_SECRET",
    "MIN_JWT_SECRET_LENGTH",
    "BcryptPasswordHasher",
    "ExpiredToken",
    "InsecureJwtSecret",
    "InvalidToken",
    "JwtAuthMiddleware",
    "PasswordHashError",
    "PasswordTooLong",
    "TokenManager",
    "TokenPayload",
    "assert_deployable_jwt_secret",
    "extract_bearer_token",
    "extract_cookie_token",
    "get_request_user",
    "hash_password",
    "resolve_token",
    "verify_password",
]
