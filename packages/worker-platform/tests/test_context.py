from uuid import UUID

from worker_platform.context import (
    correlation_context,
    get_correlation_id,
    get_tenant_id,
    normalize_correlation_id,
    tenant_context,
)


def test_context_is_scoped_and_resets() -> None:
    assert get_correlation_id() is None
    assert get_tenant_id() is None

    with correlation_context("request-correlation"), tenant_context("tenant-1"):
        assert get_correlation_id() == "request-correlation"
        assert get_tenant_id() == "tenant-1"

    assert get_correlation_id() is None
    assert get_tenant_id() is None


def test_invalid_correlation_id_is_replaced() -> None:
    correlation_id = normalize_correlation_id("not-a-uuid")

    assert str(UUID(correlation_id)) == correlation_id
