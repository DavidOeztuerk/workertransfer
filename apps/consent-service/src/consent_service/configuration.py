"""Consent-service configuration (dev defaults — override via WORKER_* env vars)."""

from __future__ import annotations

from pydantic import SecretStr
from worker_platform.configuration import PlatformSettings


class ConsentServiceSettings(PlatformSettings):
    service_name: str = "consent-service"
    port: int = 8002

    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/consent"

    jwt_secret: SecretStr = SecretStr("dev-only-secret-change-me-in-production-32bytes")
