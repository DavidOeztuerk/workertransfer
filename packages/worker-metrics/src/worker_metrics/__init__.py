"""Prometheus metrics: Custom metrics, Histograms, Counters, Gauges."""

from prometheus_client import (
    REGISTRY,
    CollectorRegistry,
    Counter,
    Gauge,
    Histogram,
    generate_latest,
)
from starlette.responses import Response

REQUEST_COUNT = Counter(
    "http_requests_total", "Total HTTP requests", ["service", "method", "path", "status"]
)

REQUEST_DURATION = Histogram(
    "http_request_duration_seconds",
    "HTTP request duration",
    ["service", "method", "path"],
    buckets=[0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0],
)

ACTIVE_CONNECTIONS = Gauge("active_connections", "Active connections", ["service"])

DB_QUERY_DURATION = Histogram(
    "db_query_duration_seconds",
    "Database query duration",
    ["service", "operation"],
    buckets=[0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0],
)

CACHE_HITS = Counter("cache_hits_total", "Cache hits", ["service", "cache_type"])

CACHE_MISSES = Counter("cache_misses_total", "Cache misses", ["service", "cache_type"])


def record_request(service: str, method: str, path: str, status: int, duration: float) -> None:
    REQUEST_COUNT.labels(service=service, method=method, path=path, status=status).inc()
    REQUEST_DURATION.labels(service=service, method=method, path=path).observe(duration)


def record_db_query(service: str, operation: str, duration: float) -> None:
    DB_QUERY_DURATION.labels(service=service, operation=operation).observe(duration)


def record_cache_hit(service: str, cache_type: str) -> None:
    CACHE_HITS.labels(service=service, cache_type=cache_type).inc()


def record_cache_miss(service: str, cache_type: str) -> None:
    CACHE_MISSES.labels(service=service, cache_type=cache_type).inc()


def metrics_endpoint(registry: CollectorRegistry | None = None) -> Response:
    return Response(
        content=generate_latest(registry if registry is not None else REGISTRY),
        media_type="text/plain",
    )
