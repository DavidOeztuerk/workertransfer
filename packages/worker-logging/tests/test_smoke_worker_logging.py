"""Smoke tests for worker-logging (Phase 1.5).

Exercises ``ContextJsonFormatter.format`` with a synthetic ``LogRecord`` (pure:
builds a JSON payload, enriches with correlation/tenant IDs from the re-exported
``worker_correlation`` helpers when set). ``configure_logging`` mutates the
global ``workertransfer`` logger (adds a handler) and is intentionally not
invoked here to keep the smoke side-effect-free and idempotent.
"""

import json
import logging

from worker_logging import ContextJsonFormatter


def _make_record(message: str = "hello") -> logging.LogRecord:
    return logging.LogRecord(
        name="workertransfer",
        level=logging.INFO,
        pathname=__file__,
        lineno=1,
        msg=message,
        args=(),
        exc_info=None,
    )


def test_smoke_formatter_emits_json_payload() -> None:
    formatter = ContextJsonFormatter()
    output = formatter.format(_make_record("smoke"))

    payload = json.loads(output)

    assert payload["message"] == "smoke"
    assert payload["level"] == "INFO"
    assert payload["logger"] == "workertransfer"
    assert "timestamp" in payload


def test_smoke_formatter_omits_optional_context_when_unset() -> None:
    formatter = ContextJsonFormatter()
    payload = json.loads(formatter.format(_make_record()))

    assert "correlation_id" not in payload
    assert "tenant_id" not in payload
