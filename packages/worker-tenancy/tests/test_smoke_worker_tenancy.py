"""Smoke tests for worker-tenancy (Phase 1.5).

Exercises the tenant-resolver hierarchy — ``NoTenantResolver`` (returns ``None``),
``HeaderTenantResolver`` against a plain headers ``Request``, and the tenant
context helpers (``set_tenant_id`` / ``get_tenant_id`` round-trip). The
``HeaderTenantResolver.resolve`` needs a well-formed UUID header to return a
tenant; no network is involved. ``ClaimTenantResolver`` inspects ``request.state``
and is also pure.
"""

from starlette.requests import Request
from worker_tenancy import (
    HeaderTenantResolver,
    NoTenantResolver,
    get_tenant_id,
    set_tenant_id,
)


def test_smoke_no_tenant_resolver_returns_none() -> None:
    resolver = NoTenantResolver()
    scope = {"type": "http", "headers": [], "method": "GET"}
    request = Request(scope)

    assert resolver.resolve(request) is None


def test_smoke_header_resolver_reads_tenant_uuid() -> None:
    import uuid

    tenant = uuid.uuid4()
    resolver = HeaderTenantResolver(header_name="X-Tenant-ID")
    scope = {
        "type": "http",
        "method": "GET",
        "headers": [(b"x-tenant-id", str(tenant).encode())],
    }
    request = Request(scope)

    assert resolver.resolve(request) == tenant


def test_smoke_header_resolver_ignores_bad_uuid() -> None:
    resolver = HeaderTenantResolver(header_name="X-Tenant-ID")
    scope = {
        "type": "http",
        "method": "GET",
        "headers": [(b"x-tenant-id", b"not-a-uuid")],
    }
    request = Request(scope)

    assert resolver.resolve(request) is None


def test_smoke_tenant_context_round_trip() -> None:
    import uuid

    set_tenant_id(None)
    tenant = uuid.uuid4()
    set_tenant_id(tenant)

    try:
        assert get_tenant_id() == tenant
    finally:
        set_tenant_id(None)
