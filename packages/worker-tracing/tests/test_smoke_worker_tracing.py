"""Smoke test for worker-tracing (Phase 1.5).

``worker-tracing`` imports OpenTelemetry instrumentors at module level and
``setup_tracing`` performs global auto-instrumentation of FastAPI/SQLAlchemy/
Redis/httpx/aio-pika plus sets a tracer provider with an OTLP exporter — NOT
exercised (heavy global mutation, points at localhost). ``start_span`` would
record against the provider. The smoke stays at module-import level and verifies
the public tracing helpers are present.
"""

import worker_tracing


def test_smoke_tracing_helpers_present() -> None:
    assert worker_tracing is not None
    for name in ("get_tracer", "setup_tracing", "start_span"):
        assert hasattr(worker_tracing, name), f"missing tracing helper: {name}"
