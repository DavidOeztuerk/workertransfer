"""Identity-service-specific configuration."""

from __future__ import annotations

from typing import Annotated, Self

from pydantic import Field, SecretStr, model_validator
from pydantic_settings import NoDecode
from worker_auth import DEV_JWT_SECRET, assert_deployable_jwt_secret
from worker_platform.configuration import PlatformSettings


class IdentityServiceSettings(PlatformSettings):
    service_name: str = "identity-service"
    port: int = 8001

    # Phase 2 security knobs (runtime-only; never committed defaults in prod).
    # "never in prod" used to be a comment only — `_reject_development_jwt_secret`
    # below makes it a startup failure.
    jwt_secret: SecretStr = SecretStr(DEV_JWT_SECRET)
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
    # Annotated[..., NoDecode] must be repeated here: overriding the field drops
    # the base class's annotation, and without it pydantic-settings json.loads
    # the raw env value again — the failure that killed both services at startup.
    cors_allow_origins: Annotated[list[str], NoDecode] = Field(
        default_factory=lambda: ["http://localhost:5173", "http://127.0.0.1:5173"]
    )

    smtp_host: str = "localhost"
    smtp_port: int = 1025
    smtp_username: str | None = None
    smtp_password: SecretStr | None = None
    smtp_use_tls: bool = False
    mail_from: str = "noreply@workertransfer.local"
    # Basis für den Bestätigungslink in der Mail. Muss die Adresse sein, die der
    # Browser sieht — nicht der Compose-Servicename.
    public_web_url: str = "http://localhost:5173"

    @model_validator(mode="after")
    def _reject_development_jwt_secret(self) -> Self:
        assert_deployable_jwt_secret(
            self.jwt_secret.get_secret_value(),
            environment=str(self.environment),
            service_name=self.service_name,
        )
        return self
