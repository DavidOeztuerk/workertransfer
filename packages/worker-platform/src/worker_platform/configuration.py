"""Typed, environment-backed settings shared by deployable services."""

from __future__ import annotations

from enum import StrEnum

from pydantic_settings import BaseSettings, SettingsConfigDict


class Environment(StrEnum):
    LOCAL = "local"
    DEVELOPMENT = "development"
    TEST = "test"
    STAGING = "staging"
    PRODUCTION = "production"


class PlatformSettings(BaseSettings):
    """Settings safe to share between services; secrets remain service-specific."""

    model_config = SettingsConfigDict(
        case_sensitive=False,
        env_file=".env",
        env_file_encoding="utf-8",
        env_prefix="WORKER_",
        extra="ignore",
    )

    service_name: str = "unnamed-service"
    service_version: str = "0.1.0"
    environment: Environment = Environment.LOCAL
    host: str = "127.0.0.1"
    port: int = 8000
    enable_docs: bool = False
    allow_development_tenant_header: bool = False
    # CORS (Sub-step 2.8 enabler): default-off so production is unchanged. When
    # non-empty and environment != PRODUCTION, create_api_app registers a
    # CORSMiddleware with allow_credentials=True so the browser accepts the
    # HTTP-only auth cookies set by POST /auth/login across the Vite↔service
    # origin boundary. In PRODUCTION this is ignored (same-origin behind the
    # gateway, ULTRAPLAN Phase 10). Configure via WORKER_CORS_ALLOW_ORIGINS.
    cors_allow_origins: list[str] = []
    cors_allow_credentials: bool = True
    cors_allow_methods: list[str] = ["GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS"]
    cors_allow_headers: list[str] = ["content-type", "authorization"]
    cors_expose_headers: list[str] = []
