"""Structured JSON logging with correlation IDs, trace IDs, and context enrichment."""

import logging

from worker_correlation import get_correlation_id, get_tenant_id


class ContextJsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        import json
        from datetime import UTC, datetime

        payload = {
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


def configure_logging(level: str = "INFO") -> None:
    logger = logging.getLogger("workertransfer")
    logger.setLevel(getattr(logging, level))
    logger.propagate = False

    handler = logging.StreamHandler()
    handler.setFormatter(ContextJsonFormatter())
    logger.addHandler(handler)
