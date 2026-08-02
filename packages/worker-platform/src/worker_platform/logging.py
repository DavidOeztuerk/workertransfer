"""Structured application logging enriched with request context."""

from __future__ import annotations

import json
import logging
from datetime import UTC, datetime
from typing import Any

from worker_platform.context import get_correlation_id, get_tenant_id


class ContextJsonFormatter(logging.Formatter):
    """A small dependency-free JSON formatter for service logs."""

    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, Any] = {
            "timestamp": datetime.now(UTC).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        if correlation_id := get_correlation_id():
            payload["correlation_id"] = correlation_id
        if tenant_id := get_tenant_id():
            payload["tenant_id"] = tenant_id
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        return json.dumps(payload, default=str, separators=(",", ":"))


def configure_logging(level: int | str = logging.INFO) -> None:
    """Configure the WorkerTransfer logger once without touching root logging.

    Idempotent in the part that matters: the handler is attached at most once
    (a second one would duplicate every record). The level *is* re-applied on
    every call, so a later caller can raise or lower verbosity without having to
    reach into the logger itself.
    """

    logger = logging.getLogger("workertransfer")
    logger.setLevel(level)
    logger.propagate = False

    if any(getattr(handler, "_workertransfer_handler", False) for handler in logger.handlers):
        return

    handler = logging.StreamHandler()
    handler._workertransfer_handler = True  # type: ignore[attr-defined]
    handler.setFormatter(ContextJsonFormatter())
    logger.addHandler(handler)
