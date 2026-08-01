"""HTTP endpoints for the Profile Service."""

from __future__ import annotations

from fastapi import APIRouter


def build_router() -> APIRouter:
    router = APIRouter(prefix="/profile_service", tags=["profile-service"])

    @router.get("/example")
    async def example() -> dict[str, str]:
        return {"service": "profile-service"}

    return router


__all__ = ["build_router"]
