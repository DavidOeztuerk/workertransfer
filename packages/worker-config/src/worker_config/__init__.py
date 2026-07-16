"""Configuration management — thin re-export of the canonical settings family.

Re-exports :mod:`worker_platform.configuration` (the platform kernel's settings
family, ADR-0005) so services can depend on ``worker_config.PlatformSettings``
without a direct worker-platform import, and still get ``BaseSettings`` /
``SettingsConfigDict`` for service-specific subclasses. This package no longer
re-defines a competing settings model; the single source of truth lives in the
platform kernel.

Related: ADR-0002 (worker-platform = kernel), ADR-0005 (canon resolution).
"""

from pydantic_settings import BaseSettings, SettingsConfigDict
from worker_platform.configuration import Environment, PlatformSettings

__all__ = [
    "BaseSettings",
    "Environment",
    "PlatformSettings",
    "SettingsConfigDict",
]
