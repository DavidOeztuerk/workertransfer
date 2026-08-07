"""Correlation ID propagation via contextvars and middleware.

Thin re-export layer over :mod:`worker_platform.context`, the canonical
implementation of request-correlation and tenant-context propagation
(ADR-0005). This package no longer carries a private implementation; it keeps
its import surface stable so consumers (e.g. ``worker-logging``) keep working
while the single source of truth lives in the platform kernel.

Related: ADR-0002 (worker-platform = kernel), ADR-0005 (canon resolution).
"""

from worker_platform.context import (
    correlation_context,
    get_correlation_id,
    get_tenant_id,
    new_correlation_id,
    normalize_correlation_id,
    tenant_context,
)

__all__ = [
    "correlation_context",
    "get_correlation_id",
    "get_tenant_id",
    "new_correlation_id",
    "normalize_correlation_id",
    "tenant_context",
]
