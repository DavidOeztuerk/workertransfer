"""Profile Service configuration.

Extends the platform settings base (ADR-0002). Every field is read from the
environment with the WORKER_ prefix — WORKER_PORT, WORKER_DATABASE_URL,
WORKER_JWT_SECRET. A variable without that prefix is silently ignored.
"""

from __future__ import annotations

from typing import Self

from pydantic import SecretStr, model_validator
from worker_ai import ANTHROPIC_MESSAGES_URL
from worker_auth import DEV_JWT_SECRET, assert_deployable_jwt_secret
from worker_platform.configuration import PlatformSettings


class ProfileServiceSettings(PlatformSettings):
    service_name: str = "profile-service"
    port: int = 8000

    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/profile_service"

    # HS256 secret issued by identity-service; this service only verifies
    # (ADR-0007). Runtime-only — never commit a real value.
    jwt_secret: SecretStr = SecretStr(DEV_JWT_SECRET)

    # Wo der Consent-Ledger erreichbar ist. Im Compose-Netz der Servicename,
    # nicht localhost — der Aufruf läuft container-zu-container.
    consent_base_url: str = "http://127.0.0.1:8002"

    # Gemeinsames Geheimnis für `POST /internal/erasure` (ADR-0027 §4.4).
    # **Ausdrücklich ein anderes als `notify_secret`**: „darf eine Mail
    # anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
    # dasselbe Papier sein.
    #
    # Leer heißt: der Endpunkt ist zu. Bis es echte Dienstidentitäten gibt, ist
    # das die Zwischenlösung — und die Voreinstellung schließt.
    erasure_secret: SecretStr = SecretStr("")

    # Der Entwurfsdienst. **Leer heißt: die Funktion ist aus** — nicht „offen
    # für alle", nicht „nimm einen Standardschlüssel". Ohne Schlüssel ruft der
    # Dienst keinen fremden Anbieter an, und die Oberfläche sagt das (ADR-0024).
    #
    # Der Schlüssel steht ausschließlich in der Umgebung. Er landet nie im
    # Repository und nie in einem Protokoll (`product-scope.md`).
    anthropic_api_key: SecretStr = SecretStr("")
    drafting_model: str = "claude-sonnet-5"
    # Wohin der Aufruf geht. Ein Gateway oder Proxy, der dieselbe Messages-API
    # spricht, lässt sich hier eintragen. **Kein Provider-Wechsel** — wer dort
    # etwas hinstellt, das anders antwortet, bekommt einen ehrlichen Fehler.
    drafting_base_url: str = ANTHROPIC_MESSAGES_URL

    @model_validator(mode="after")
    def _reject_development_jwt_secret(self) -> Self:
        assert_deployable_jwt_secret(
            self.jwt_secret.get_secret_value(),
            environment=str(self.environment),
            service_name=self.service_name,
        )
        return self
