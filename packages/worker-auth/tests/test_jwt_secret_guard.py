"""The signing-secret guard refuses a development key in a deployed environment."""

from __future__ import annotations

import pytest
from worker_auth import (
    DEV_JWT_SECRET,
    MIN_JWT_SECRET_LENGTH,
    InsecureJwtSecret,
    assert_deployable_jwt_secret,
)

_REAL_SECRET = "y" * MIN_JWT_SECRET_LENGTH


@pytest.mark.parametrize("environment", ["production", "staging", "PRODUCTION", "Staging"])
def test_rejects_the_committed_default_in_deployed_environments(environment: str) -> None:
    with pytest.raises(InsecureJwtSecret) as excinfo:
        assert_deployable_jwt_secret(
            DEV_JWT_SECRET, environment=environment, service_name="consent-service"
        )
    # The message has to name the service and the variable, or whoever reads the
    # crash at 3am cannot act on it.
    assert "consent-service" in str(excinfo.value)
    assert "WORKER_JWT_SECRET" in str(excinfo.value)


@pytest.mark.parametrize("environment", ["local", "development", "test"])
def test_allows_the_default_outside_deployed_environments(environment: str) -> None:
    # A fresh clone must still run without setting anything.
    assert_deployable_jwt_secret(
        DEV_JWT_SECRET, environment=environment, service_name="identity-service"
    )


def test_rejects_a_short_but_custom_secret_in_production() -> None:
    with pytest.raises(InsecureJwtSecret):
        assert_deployable_jwt_secret(
            "x" * (MIN_JWT_SECRET_LENGTH - 1),
            environment="production",
            service_name="identity-service",
        )


def test_accepts_a_real_secret_in_production() -> None:
    assert_deployable_jwt_secret(
        _REAL_SECRET, environment="production", service_name="identity-service"
    )


def test_short_secret_is_tolerated_outside_deployed_environments() -> None:
    assert_deployable_jwt_secret("short", environment="local", service_name="identity-service")
