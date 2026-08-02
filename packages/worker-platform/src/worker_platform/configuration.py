"""Typed, environment-backed settings shared by deployable services."""

from __future__ import annotations

from enum import StrEnum
from typing import Annotated

from pydantic import field_validator
from pydantic_settings import BaseSettings, NoDecode, SettingsConfigDict


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
    # NoDecode: without it pydantic-settings runs json.loads on the raw env value
    # for any list field, so WORKER_CORS_ALLOW_ORIGINS=http://a,http://b raises a
    # SettingsError inside the *source* — before any validator could see it, and
    # early enough to kill the process at import time. That is exactly what
    # scripts/run-dev.sh hit: both services died on startup with
    # "error parsing value for field cors_allow_origins".
    cors_allow_origins: Annotated[list[str], NoDecode] = []
    cors_allow_credentials: bool = True
    cors_allow_methods: Annotated[list[str], NoDecode] = [
        "GET",
        "POST",
        "PATCH",
        "PUT",
        "DELETE",
        "OPTIONS",
    ]
    cors_allow_headers: Annotated[list[str], NoDecode] = ["content-type", "authorization"]
    cors_expose_headers: Annotated[list[str], NoDecode] = []

    @field_validator(
        "cors_allow_origins",
        "cors_allow_methods",
        "cors_allow_headers",
        "cors_expose_headers",
        mode="before",
    )
    @classmethod
    def _accept_csv_or_json(cls, value: object) -> object:
        """Accept a comma-separated env value as well as a JSON array.

        A human writing an origin allowlist into a shell or a .env file writes
        ``a,b``; requiring ``["a","b"]`` there is a trap that fails at startup
        rather than at review. JSON keeps working for anything generated.
        """
        if not isinstance(value, str):
            return value
        text = value.strip()
        if text.startswith("["):
            import json

            return json.loads(text)
        if not text:
            return []
        return [item.strip() for item in text.split(",") if item.strip()]
