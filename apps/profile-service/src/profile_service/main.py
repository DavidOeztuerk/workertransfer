"""Deployable entry point for the Profile Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from profile_service.configuration import ProfileServiceSettings
from profile_service.presentation.compose_api import build_app


def create_app(settings: ProfileServiceSettings | None = None) -> FastAPI:
    return build_app(settings or ProfileServiceSettings())


app = create_app()


def run() -> None:
    settings = ProfileServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
