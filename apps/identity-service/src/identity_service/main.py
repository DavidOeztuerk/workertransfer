"""Deployable entry point for the identity service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI
from worker_platform.presentation.app import create_api_app

from identity_service.configuration import IdentityServiceSettings


def create_app(settings: IdentityServiceSettings | None = None) -> FastAPI:
    return create_api_app(settings or IdentityServiceSettings())


app = create_app()


def run() -> None:
    settings = IdentityServiceSettings()
    uvicorn.run(create_app(settings), host=settings.host, port=settings.port)
