"""Composition-root for the consent-service HTTP API (placeholder — Task 9)."""

from __future__ import annotations

from fastapi import FastAPI

from consent_service.configuration import ConsentServiceSettings


def build_app(settings: ConsentServiceSettings) -> FastAPI:
    app = FastAPI(title="WorkerTransfer Consent Service", version="0.1.0")

    @app.get("/health/live")
    async def health_live() -> dict[str, str]:
        return {"status": "ok"}

    return app
