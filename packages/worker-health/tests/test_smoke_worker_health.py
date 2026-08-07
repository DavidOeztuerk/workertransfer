"""Smoke tests for worker-health (Phase 1.5).

Exercises the pure surface — ``HealthStatus`` enum, ``HealthCheckResult``
dataclass, and ``run_health_checks`` with an empty list (aggregates to healthy
overall with no network). The concrete ``DatabaseHealthCheck`` /
``RedisHealthCheck`` / ``RabbitMQHealthCheck`` hit the network and are NOT
touched.
"""

from worker_health import HealthCheckResult, HealthStatus, run_health_checks


def test_smoke_health_status_and_result() -> None:
    result = HealthCheckResult(name="smoke", status=HealthStatus.HEALTHY)

    assert result.name == "smoke"
    assert result.status is HealthStatus.HEALTHY
    assert HealthStatus.UNHEALTHY.value == "unhealthy"


async def test_smoke_run_health_checks_empty() -> None:
    summary = await run_health_checks(checks=[])

    assert summary["status"] == "healthy"
    assert summary["checks"] == {}
