"""Identity-service-specific configuration."""

from __future__ import annotations

from pydantic import SecretStr
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
