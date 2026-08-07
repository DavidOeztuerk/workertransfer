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

    # Gemeinsames Geheimnis für `POST /notifications`: Dienste haben es aus der
    # Umgebung, ein Browser nicht. Ausdrücklich eine Zwischenlösung, bis es
    # echte Dienstidentitäten gibt (Härtung, Phase 10) — ohne sie wäre der
    # Endpunkt ein Weg, beliebigen Menschen Mails zu schicken.
    #
    # Leer heißt: der Endpunkt ist zu. Nicht „offen für alle" — eine
    # Voreinstellung, die im Zweifel öffnet, ist genau die falsche.
    notify_secret: SecretStr = SecretStr("")

    # Gemeinsames Geheimnis für die Löschkaskade (ADR-0027 §4.4).
    # **Ausdrücklich ein anderes als `notify_secret`**: „darf eine Mail
    # anstoßen" und „darf alles über einen Menschen löschen" dürfen nicht
    # dasselbe Papier sein. Leer heißt: es wird nichts zugestellt — und zwar
    # als *Fehlschlag*, nicht als stilles Überspringen. Eine Zeile, die als
    # zugestellt gälte, obwohl niemand gelöscht hat, wäre der schlimmste Fall.
    erasure_secret: SecretStr = SecretStr("")

    # Wohin die Löschbefehle gehen. Im Compose-Netz die Servicenamen.
    # jobs-service ist **kein** Empfänger der Kaskade — er hält nichts
    # Personenbezogenes (ADR-0027 §2) und bekommt nur die Absicht aus §7,
    # wenn ein Unternehmen ohne Administrator zurückbleibt.
    consent_base_url: str = "http://127.0.0.1:8002"
    profile_base_url: str = "http://127.0.0.1:8003"
    resume_base_url: str = "http://127.0.0.1:8004"
    portfolio_base_url: str = "http://127.0.0.1:8005"
    applications_base_url: str = "http://127.0.0.1:8007"
    transfer_base_url: str = "http://127.0.0.1:8009"
    github_base_url: str = "http://127.0.0.1:8010"
    jobs_base_url: str = "http://127.0.0.1:8006"

    # Wie oft der Löschzusteller nachsieht, und wie weit der Abstand wachsen
    # darf, solange nichts vorangeht (ADR-0027 §4.3). Ohne Versuchsobergrenze
    # braucht es das: wer nie aufgibt, darf nicht im Sekundentakt gegen eine
    # Wand laufen.
    erasure_interval_seconds: float = 5.0
    erasure_max_interval_seconds: float = 300.0

    # Bremse am Auth-Rand. `None` heißt „aus der Umgebung ableiten": in LOCAL
    # und TEST aus, sonst an.
    #
    # Aus in LOCAL, weil im Compose-Stack **jede** Browser-Anfrage von derselben
    # Gateway-Adresse kommt. Eine Bremse je Herkunft würde dort die gesamte
    # Testreihe als einen Angreifer behandeln — und wäre damit keine
    # Sicherheitsmaßnahme, sondern nur ein Grund, sie wieder auszubauen.
    # Ausdrücklich einschaltbar (`WORKER_AUTH_THROTTLE_ENABLED=true`), damit man
    # sie auch lokal sehen kann.
    auth_throttle_enabled: bool | None = None
    # Nur setzen, wenn wirklich ein Proxy davorsteht. `X-Forwarded-For` ist frei
    # wählbar; ihm ungefragt zu glauben hieße, gar keine Bremse zu haben.
    trust_forwarded_for: bool = False

    @model_validator(mode="after")
    def _reject_development_jwt_secret(self) -> Self:
        assert_deployable_jwt_secret(
            self.jwt_secret.get_secret_value(),
            environment=str(self.environment),
            service_name=self.service_name,
        )
        return self
