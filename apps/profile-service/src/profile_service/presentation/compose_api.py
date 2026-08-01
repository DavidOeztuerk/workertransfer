"""Composition-Root for the Profile Service HTTP app (ADR-0003).

Explicit, not a fluent builder: a reader sees in this one file exactly which
cross-cutting behaviours are active and in which order. The kernel owns the
middleware order (correlation -> auth -> tenant -> security); this service
supplies only its own routers and adapters.
"""

from __future__ import annotations

from fastapi import FastAPI
from profile_service.configuration import ProfileServiceSettings
from profile_service.presentation.http.router import build_router
from worker_platform.presentation.app import create_api_app


def build_app(settings: ProfileServiceSettings) -> FastAPI:
    return create_api_app(settings, routers=(build_router(),))


__all__ = ["build_app"]
