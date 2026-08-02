"""Portfolio Service configuration.

Extends the platform settings base (ADR-0002). Every field is read from the
environment with the WORKER_ prefix — WORKER_PORT, WORKER_DATABASE_URL,
WORKER_JWT_SECRET. A variable without that prefix is silently ignored.
"""

from __future__ import annotations

from typing import Self

from pydantic import SecretStr, model_validator
from worker_auth import DEV_JWT_SECRET, assert_deployable_jwt_secret
from worker_platform.configuration import PlatformSettings


class PortfolioServiceSettings(PlatformSettings):
    service_name: str = "portfolio-service"
    port: int = 8000

    database_url: str = "postgresql+asyncpg://worker:worker@127.0.0.1:5432/portfolio"

    # Wo der Consent-Ledger erreichbar ist. Im Compose-Netz der Servicename,
    # nicht localhost — der Aufruf läuft container-zu-container.
    consent_base_url: str = "http://127.0.0.1:8002"

    # HS256 secret issued by identity-service; this service only verifies
    # (ADR-0007). Runtime-only — never commit a real value.
    jwt_secret: SecretStr = SecretStr(DEV_JWT_SECRET)

    @model_validator(mode="after")
    def _reject_development_jwt_secret(self) -> Self:
        """Das Entwicklungs-Secret darf nirgends deployed werden.

        Ohne diese Prüfung startet ein Service in production fröhlich mit einem
        Secret, das im Repository steht — und jeder kann sich Tokens ausstellen.
        Die Prüfung greift nur in production/staging, damit lokal nichts im Weg
        steht.
        """
        assert_deployable_jwt_secret(
            self.jwt_secret.get_secret_value(),
            environment=str(self.environment),
            service_name=self.service_name,
        )
        return self
