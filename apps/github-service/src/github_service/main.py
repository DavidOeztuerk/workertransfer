"""Deployable entry point for the Github Service."""

from __future__ import annotations

import uvicorn
from fastapi import FastAPI

from github_service.configuration import GithubServiceSettings
from github_service.presentation.compose_api import build_app


def create_app(settings: GithubServiceSettings | None = None) -> FastAPI:
    return build_app(settings or GithubServiceSettings())


app = create_app()


def run() -> None:
    settings = GithubServiceSettings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)
