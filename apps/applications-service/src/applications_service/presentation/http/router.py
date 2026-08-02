"""HTTP endpoints for the Applications Service."""

from __future__ import annotations

from fastapi import APIRouter


def build_router() -> APIRouter:
    router = APIRouter(prefix="/applications_service", tags=["applications-service"])

    @router.get("/example")
    async def example() -> dict[str, str]:
        return {"service": "applications-service"}

    return router


__all__ = ["build_router"]
