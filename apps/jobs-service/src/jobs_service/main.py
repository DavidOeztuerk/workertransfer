"""Deployable entry point for the Jobs Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from jobs_service.configuration import JobsServiceSettings
from jobs_service.presentation.compose_api import build_app


def create_app(settings: JobsServiceSettings | None = None) -> FastAPI:
    return build_app(settings or JobsServiceSettings())


app = create_app()


def run() -> None:
    settings = JobsServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
