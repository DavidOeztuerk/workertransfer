"""Deployable entry point for the Companies Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from companies_service.configuration import CompaniesServiceSettings
from companies_service.presentation.compose_api import build_app


def create_app(settings: CompaniesServiceSettings | None = None) -> FastAPI:
    return build_app(settings or CompaniesServiceSettings())


app = create_app()


def run() -> None:
    settings = CompaniesServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
