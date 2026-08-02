"""Deployable entry point for the Resume Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from resume_service.configuration import ResumeServiceSettings
from resume_service.presentation.compose_api import build_app


def create_app(settings: ResumeServiceSettings | None = None) -> FastAPI:
    return build_app(settings or ResumeServiceSettings())


app = create_app()


def run() -> None:
    settings = ResumeServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
