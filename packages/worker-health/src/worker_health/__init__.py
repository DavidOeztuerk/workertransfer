"""Health checks: Liveness, Readiness, Startup, Dependency checks."""

from dataclasses import dataclass
from enum import Enum
from typing import Any, Protocol, cast


class HealthStatus(Enum):
    HEALTHY = "healthy"
    DEGRADED = "degraded"
    UNHEALTHY = "unhealthy"


@dataclass
class HealthCheckResult:
    name: str
    status: HealthStatus
    message: str | None = None
    details: dict[str, Any] | None = None


class HealthCheck(Protocol):
    async def check(self) -> HealthCheckResult: ...


class DatabaseHealthCheck:
    def __init__(self, session_factory: Any) -> None:
        self._session_factory = session_factory

    async def check(self) -> HealthCheckResult:
        try:
            async with self._session_factory() as session:
                await session.execute("SELECT 1")
            return HealthCheckResult(name="database", status=HealthStatus.HEALTHY)
        except Exception as e:
            return HealthCheckResult(name="database", status=HealthStatus.UNHEALTHY, message=str(e))


class RedisHealthCheck:
    def __init__(self, redis_url: str) -> None:
        self._redis_url = redis_url

    async def check(self) -> HealthCheckResult:
        try:
            import redis.asyncio as redis

            client = redis.from_url(self._redis_url)  # type: ignore[no-untyped-call]
            await client.ping()
            await client.close()
            return HealthCheckResult(name="redis", status=HealthStatus.HEALTHY)
        except Exception as e:
            return HealthCheckResult(name="redis", status=HealthStatus.UNHEALTHY, message=str(e))


class RabbitMQHealthCheck:
    def __init__(self, url: str) -> None:
        self._url = url

    async def check(self) -> HealthCheckResult:
        try:
            import aio_pika

            conn = await cast("Any", aio_pika).connect_robust(self._url)
            await conn.close()
            return HealthCheckResult(name="rabbitmq", status=HealthStatus.HEALTHY)
        except Exception as e:
            return HealthCheckResult(name="rabbitmq", status=HealthStatus.UNHEALTHY, message=str(e))


async def run_health_checks(checks: list[HealthCheck]) -> dict[str, Any]:
    results: dict[str, Any] = {}
    overall = HealthStatus.HEALTHY
    for check in checks:
        result = await check.check()
        results[result.name] = {
            "status": result.status.value,
            "message": result.message,
            "details": result.details,
        }
        if result.status == HealthStatus.UNHEALTHY:
            overall = HealthStatus.UNHEALTHY
        elif result.status == HealthStatus.DEGRADED and overall == HealthStatus.HEALTHY:
            overall = HealthStatus.DEGRADED
    return {"status": overall.value, "checks": results}
