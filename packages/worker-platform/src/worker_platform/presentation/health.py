"""Kubernetes-compatible liveness and readiness endpoints."""

from __future__ import annotations

from collections.abc import Iterable
from typing import Protocol

from fastapi import APIRouter
from fastapi.responses import JSONResponse


class ReadinessCheck(Protocol):
    name: str

    async def check(self) -> None: ...


def create_health_router(
    service_name: str, readiness_checks: Iterable[ReadinessCheck] = ()
) -> APIRouter:
    checks = tuple(readiness_checks)
    router = APIRouter(include_in_schema=False)

    @router.get("/health/live")
    async def liveness() -> dict[str, str]:
        return {"status": "ok", "service": service_name}

    @router.get("/health/ready")
    async def readiness() -> JSONResponse:
        failed_checks: list[str] = []
        for check in checks:
            try:
                await check.check()
            except Exception:
                failed_checks.append(check.name)

        if failed_checks:
            return JSONResponse(
                status_code=503,
                content={"status": "unavailable", "service": service_name, "checks": failed_checks},
            )
        return JSONResponse(status_code=200, content={"status": "ok", "service": service_name})

    return router
