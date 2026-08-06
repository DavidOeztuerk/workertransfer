"""Consent-service configuration (dev defaults — override via WORKER_* env vars)."""

from __future__ import annotations

from typing import Self

from pydantic import SecretStr, model_validator
from worker_auth import DEV_JWT_SECRET, assert_deployable_jwt_secret
from worker_platform.configuration import PlatformSettings


class ConsentServiceSettings(PlatformSettings):
    service_name: str = "consent-service"
    port: int = 8002

    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/consent"

    # This service only verifies identity-service's tokens (ADR-0015), so the
    # value must match the issuer's — which is exactly why an unset variable
    # here is as dangerous as an unset one there.
    jwt_secret: SecretStr = SecretStr(DEV_JWT_SECRET)

    # Gemeinsames Geheimnis für `POST /internal/erasure` (ADR-0027 §4.4).
    # **Ausdrücklich ein anderes als `notify_secret`**: „darf eine Mail
    # anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
    # dasselbe Papier sein.
    #
    # Leer heißt: der Endpunkt ist zu. Bis es echte Dienstidentitäten gibt, ist
    # das die Zwischenlösung — und die Voreinstellung schließt.
    erasure_secret: SecretStr = SecretStr("")

    @model_validator(mode="after")
    def _reject_development_jwt_secret(self) -> Self:
        assert_deployable_jwt_secret(
            self.jwt_secret.get_secret_value(),
            environment=str(self.environment),
            service_name=self.service_name,
        )
        return self
