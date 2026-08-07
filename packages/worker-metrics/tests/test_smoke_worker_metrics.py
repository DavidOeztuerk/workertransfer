"""Smoke tests for worker-metrics (Phase 1.5).

Exercises the prometheus recorder helpers (pure counter/histogram increments) and
the ``metrics_endpoint`` factory (builds a Starlette ``Response`` with the text
exposition; no network). Counters are module-level singletons registered in the
global ``REGISTRY`` at import — incrementing them is idempotent and network-free.
"""

from worker_metrics import (
    metrics_endpoint,
    record_cache_hit,
    record_cache_miss,
    record_db_query,
    record_request,
)


def test_smoke_recorders_increment_without_error() -> None:
    record_request(service="smoke", method="GET", path="/x", status=200, duration=0.01)
    record_db_query(service="smoke", operation="select", duration=0.002)
    record_cache_hit(service="smoke", cache_type="memory")
    record_cache_miss(service="smoke", cache_type="memory")


def test_smoke_metrics_endpoint_returns_response() -> None:
    response = metrics_endpoint()

    assert response.media_type == "text/plain"
    assert response.body  # non-empty prometheus exposition
