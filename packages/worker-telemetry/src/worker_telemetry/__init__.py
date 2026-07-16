"""Unified telemetry: Logging + Metrics + Tracing integration."""

from __future__ import annotations

from worker_logging import configure_logging
from worker_metrics import record_cache_hit, record_cache_miss, record_db_query, record_request
from worker_tracing import Tracer, get_tracer, setup_tracing

__all__ = [
    "configure_logging",
    "get_tracer",
    "record_cache_hit",
    "record_cache_miss",
    "record_db_query",
    "record_request",
    "setup_telemetry",
    "setup_tracing",
]


def setup_telemetry(
    service_name: str, otlp_endpoint: str = "http://localhost:4317", log_level: str = "INFO"
) -> Tracer:
    configure_logging(log_level)
    setup_tracing(service_name, otlp_endpoint)
    return get_tracer(service_name)
