"""Identity-service-specific configuration."""

from __future__ import annotations

from pydantic import Field, SecretStr
from worker_platform.configuration import PlatformSettings


class IdentityServiceSettings(PlatformSettings):
    service_name: str = "identity-service"
    port: int = 8001

    # Phase 2 security knobs (runtime-only; never committed defaults in prod).
    jwt_secret: SecretStr = SecretStr("dev-only-secret-change-me-in-production-32bytes")
    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/identity"
    jwt_access_token_expire_minutes: int = 15
    jwt_refresh_token_expire_minutes: int = 1440
    bcrypt_rounds: int = 12

    # CORS dev allowlist (Sub-step 2.8): the web client at the Vite dev server
    # posts credentials to /auth/login; browsers reject the HTTP-only cookie
    # response unless the service answers the cross-origin preflight. LOCAL/DEV/
    # TEST/STAGING only — the platform hook refuses CORS in PRODUCTION
    # (same-origin behind the gateway, ULTRAPLAN Phase 10). Override via
    # WORKER_CORS_ALLOW_ORIGINS in any environment (the env value replaces the
    # default list; pydantic-settings parses JSON or comma-separated origins).
    cors_allow_origins: list[str] = Field(
        default_factory=lambda: ["http://localhost:5173", "http://127.0.0.1:5173"]
    )
