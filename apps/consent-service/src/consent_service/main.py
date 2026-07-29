"""Deployable entry point for the consent service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from consent_service.configuration import ConsentServiceSettings
from consent_service.presentation.compose_api import build_app


def create_app(settings: ConsentServiceSettings | None = None) -> FastAPI:
    return build_app(settings or ConsentServiceSettings())


app = create_app()


def run() -> None:
    settings = ConsentServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
