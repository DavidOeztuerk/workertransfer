"""Smoke tests for worker-correlation (Phase 1.5).

``worker-correlation`` is a thin re-export layer over the canonical
``worker_platform.context`` (ADR-0005). The smoke verifies that the re-exported
symbols are the *same objects* as the canonical implementation (``is`` identity
— not a redefinition) and that the helpers behave correctly.
"""

import worker_correlation
from worker_platform.context import (
    correlation_context,
    get_correlation_id,
    get_tenant_id,
    normalize_correlation_id,
)


def test_smoke_reexport_identity() -> None:
    assert worker_correlation.get_correlation_id is get_correlation_id
    assert worker_correlation.get_tenant_id is get_tenant_id
    assert worker_correlation.correlation_context is correlation_context
    assert worker_correlation.normalize_correlation_id is normalize_correlation_id


def test_smoke_correlation_context_is_scoped() -> None:
    assert worker_correlation.get_correlation_id() is None

    with worker_correlation.correlation_context("req-1"):
        assert worker_correlation.get_correlation_id() == "req-1"

    assert worker_correlation.get_correlation_id() is None
