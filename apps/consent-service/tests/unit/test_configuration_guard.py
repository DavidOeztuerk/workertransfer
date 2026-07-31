"""Settings refuse to build with a development signing secret in production.

The guard function is unit-tested in worker-auth; these tests pin the *wiring*,
which is the part that silently rots when someone adds a settings field.
"""

from __future__ import annotations

import pytest
from consent_service.configuration import ConsentServiceSettings
from pydantic import SecretStr, ValidationError
from worker_auth import DEV_JWT_SECRET

_REAL_SECRET = SecretStr("n" * 48)


def test_production_refuses_the_committed_default() -> None:
    with pytest.raises(ValidationError) as excinfo:
        ConsentServiceSettings(environment="production", jwt_secret=SecretStr(DEV_JWT_SECRET))

    assert "WORKER_JWT_SECRET" in str(excinfo.value)


def test_staging_refuses_it_too() -> None:
    # Staging is internet-facing and mirrors production; a known key is just as
    # forgeable there.
    with pytest.raises(ValidationError):
        ConsentServiceSettings(environment="staging", jwt_secret=SecretStr(DEV_JWT_SECRET))


def test_production_accepts_a_real_secret() -> None:
    settings = ConsentServiceSettings(environment="production", jwt_secret=_REAL_SECRET)

    assert settings.jwt_secret.get_secret_value() == _REAL_SECRET.get_secret_value()


def test_a_fresh_clone_still_runs_locally() -> None:
    settings = ConsentServiceSettings(environment="local", jwt_secret=SecretStr(DEV_JWT_SECRET))

    assert settings.service_name == "consent-service"
