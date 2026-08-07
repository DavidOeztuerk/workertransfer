"""Smoke tests for worker-config (Phase 1.5).

``worker-config`` is a thin re-export layer over the canonical settings family
in ``worker_platform.configuration`` (ADR-0005). The smoke verifies that the
re-exported ``PlatformSettings`` and ``Environment`` are the *same objects* as
the canonical ones (``is`` identity), that ``BaseSettings`` /
``SettingsConfigDict`` come from ``pydantic_settings``, and that a settings
subclass picks up the ``WORKER_`` env prefix and defaults.
"""

import os

import worker_config
from pydantic_settings import BaseSettings as PydanticBaseSettings
from pydantic_settings import SettingsConfigDict as PydanticSettingsConfigDict
from worker_platform.configuration import Environment, PlatformSettings


def test_smoke_reexport_identity() -> None:
    assert worker_config.PlatformSettings is PlatformSettings
    assert worker_config.Environment is Environment
    assert worker_config.BaseSettings is PydanticBaseSettings
    assert worker_config.SettingsConfigDict is PydanticSettingsConfigDict


def test_smoke_platform_settings_defaults() -> None:
    settings = PlatformSettings()

    assert settings.service_name == "unnamed-service"
    assert settings.environment is Environment.LOCAL


def test_smoke_env_prefix_works() -> None:
    os.environ["WORKER_SERVICE_NAME"] = "smoke-service"
    try:
        settings = PlatformSettings()

        assert settings.service_name == "smoke-service"
    finally:
        os.environ.pop("WORKER_SERVICE_NAME", None)
