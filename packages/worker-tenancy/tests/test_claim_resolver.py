"""Tests for the scope-based ClaimTenantResolver + platform-canon re-exports (Task 20)."""

from __future__ import annotations

from types import SimpleNamespace
from uuid import UUID

from worker_tenancy import ClaimTenantResolver


def _scope_with_user(user: object | None) -> dict[str, object]:
    return {"type": "http", "state": {"user": user}}


def test_claim_resolver_reads_tenant_from_user_state() -> None:
    principal = SimpleNamespace(tenant_id=UUID("22222222-2222-2222-2222-222222002222"))
    resolver = ClaimTenantResolver()
    assert resolver.resolve(_scope_with_user(principal)) == "22222222-2222-2222-2222-222222002222"


def test_claim_resolver_returns_none_when_no_user() -> None:
    resolver = ClaimTenantResolver()
    assert resolver.resolve({"type": "http", "state": {}}) is None


def test_claim_resolver_returns_none_when_user_none() -> None:
    resolver = ClaimTenantResolver()
    assert resolver.resolve(_scope_with_user(None)) is None


def test_reexports_match_platform_canon_identity() -> None:
    import worker_platform.context as canon
    import worker_tenancy

    assert worker_tenancy.get_tenant_id is canon.get_tenant_id
    assert worker_tenancy.tenant_context is canon.tenant_context
