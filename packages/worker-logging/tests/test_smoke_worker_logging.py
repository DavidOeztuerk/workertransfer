"""worker-logging is a thin re-export of the platform canon (ADR-0014).

Two things matter here: the symbols must be *identical objects* to the canon (a
copy would drift), and `configure_logging` must stay idempotent — the deleted
local implementation attached a fresh StreamHandler on every call, so calling it
twice duplicated every log line.
"""

import json
import logging

import worker_platform.logging as canon
from worker_logging import ContextJsonFormatter, configure_logging


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


def test_reexports_are_the_platform_canon() -> None:
    assert ContextJsonFormatter is canon.ContextJsonFormatter
    assert configure_logging is canon.configure_logging


def test_smoke_formatter_emits_json_payload() -> None:
    payload = json.loads(ContextJsonFormatter().format(_make_record("smoke")))

    assert payload["message"] == "smoke"
    assert payload["level"] == "INFO"
    assert payload["logger"] == "workertransfer"
    assert "timestamp" in payload


def test_smoke_formatter_omits_optional_context_when_unset() -> None:
    payload = json.loads(ContextJsonFormatter().format(_make_record()))

    assert "correlation_id" not in payload
    assert "tenant_id" not in payload


def test_configure_logging_attaches_exactly_one_handler() -> None:
    logger = logging.getLogger("workertransfer")
    before, before_level = list(logger.handlers), logger.level
    try:
        configure_logging()
        after_first = len(logger.handlers)
        configure_logging()
        assert len(logger.handlers) == after_first, (
            "configure_logging added a second handler; every record would be logged twice"
        )
    finally:
        logger.handlers, logger.level = before, before_level


def test_configure_logging_reapplies_the_level() -> None:
    """Verbosity must stay adjustable after the first call (worker-telemetry)."""
    logger = logging.getLogger("workertransfer")
    before, before_level = list(logger.handlers), logger.level
    try:
        configure_logging()
        assert logger.level == logging.INFO
        configure_logging("DEBUG")
        assert logger.level == logging.DEBUG
    finally:
        logger.handlers, logger.level = before, before_level
