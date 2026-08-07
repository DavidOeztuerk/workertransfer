"""Deployable entry point for the Portfolio Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from portfolio_service.configuration import PortfolioServiceSettings
from portfolio_service.presentation.compose_api import build_app


def create_app(settings: PortfolioServiceSettings | None = None) -> FastAPI:
    return build_app(settings or PortfolioServiceSettings())


app = create_app()


def run() -> None:
    settings = PortfolioServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
