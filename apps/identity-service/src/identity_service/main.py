"""Deployable entry point for the identity service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from identity_service.configuration import IdentityServiceSettings
from identity_service.presentation.compose_api import build_app


def create_app(settings: IdentityServiceSettings | None = None) -> FastAPI:
    return build_app(settings or IdentityServiceSettings())


app = create_app()


def run() -> None:
    settings = IdentityServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
