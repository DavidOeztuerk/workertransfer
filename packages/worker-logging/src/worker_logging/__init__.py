"""Thin re-export of the platform logging canon (ADR-0002, ADR-0005, ADR-0014).

`worker_platform.logging` is the single implementation of structured JSON logging
enriched with correlation and tenant context. This package used to carry its own
copy whose `configure_logging` attached a fresh StreamHandler on every call — two
calls meant every record logged twice. The canonical version is idempotent via a
handler sentinel.

Kept as a re-export rather than deleted so `worker-telemetry` (its only consumer)
keeps working; new code should import `worker_platform.logging` directly.
"""

from __future__ import annotations

from worker_platform.logging import ContextJsonFormatter, configure_logging

__all__ = ["ContextJsonFormatter", "configure_logging"]
