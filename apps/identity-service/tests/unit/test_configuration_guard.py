"""Settings refuse to build with a development signing secret in production.

identity-service *issues* the tokens the whole platform trusts, so a forgeable
key here is worse than anywhere else: since ADR-0015 the same secret verifies
tokens in consent-service too.
"""

from __future__ import annotations

import pytest
from identity_service.configuration import IdentityServiceSettings
from pydantic import SecretStr, ValidationError
from worker_auth import DEV_JWT_SECRET

_REAL_SECRET = SecretStr("n" * 48)


def test_production_refuses_the_committed_default() -> None:
    with pytest.raises(ValidationError) as excinfo:
        IdentityServiceSettings(environment="production", jwt_secret=SecretStr(DEV_JWT_SECRET))

    assert "WORKER_JWT_SECRET" in str(excinfo.value)


def test_production_refuses_a_too_short_custom_secret() -> None:
    with pytest.raises(ValidationError):
        IdentityServiceSettings(environment="production", jwt_secret=SecretStr("short"))


def test_production_accepts_a_real_secret() -> None:
    settings = IdentityServiceSettings(environment="production", jwt_secret=_REAL_SECRET)

    assert settings.service_name == "identity-service"


def test_a_fresh_clone_still_runs_locally() -> None:
    settings = IdentityServiceSettings(environment="local", jwt_secret=SecretStr(DEV_JWT_SECRET))

    assert settings.port == 8001
