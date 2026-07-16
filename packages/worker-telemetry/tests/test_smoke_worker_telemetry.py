"""Smoke test for worker-telemetry (Phase 1.5).

``worker-telemetry`` is a unified facade re-exporting from ``worker_logging``,
``worker_metrics``, and ``worker_tracing`` plus ``setup_telemetry``. Calling
``setup_telemetry`` has heavy global side effects (configures the root logger and
runs OpenTelemetry auto-instrumentation of FastAPI/SQLAlchemy/Redis/httpx/aio-pika
via ``setup_tracing``) and points an OTLP exporter at localhost — NOT exercised
here. The smoke verifies the module imports and the re-export surface is present.
"""

import worker_telemetry


def test_smoke_telemetry_facade_imports() -> None:
    assert worker_telemetry is not None
    for name in ("configure_logging", "record_request", "get_tracer", "setup_tracing"):
        assert hasattr(worker_telemetry, name), f"missing re-export: {name}"
