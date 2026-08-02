"""Deployable entry point for the Transfer Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from transfer_service.configuration import TransferServiceSettings
from transfer_service.presentation.compose_api import build_app


def create_app(settings: TransferServiceSettings | None = None) -> FastAPI:
    return build_app(settings or TransferServiceSettings())


app = create_app()


def run() -> None:
    settings = TransferServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
