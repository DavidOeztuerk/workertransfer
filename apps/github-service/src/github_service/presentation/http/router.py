"""HTTP endpoints for the Github Service."""

from __future__ import annotations

from fastapi import APIRouter


def build_router() -> APIRouter:
    router = APIRouter(prefix="/github_service", tags=["github-service"])

    @router.get("/example")
    async def example() -> dict[str, str]:
        return {"service": "github-service"}

    return router


__all__ = ["build_router"]
