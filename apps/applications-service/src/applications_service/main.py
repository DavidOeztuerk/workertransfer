"""Deployable entry point for the Applications Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from applications_service.configuration import ApplicationsServiceSettings
from applications_service.presentation.compose_api import build_app


def create_app(settings: ApplicationsServiceSettings | None = None) -> FastAPI:
    return build_app(settings or ApplicationsServiceSettings())


app = create_app()


def run() -> None:
    settings = ApplicationsServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
