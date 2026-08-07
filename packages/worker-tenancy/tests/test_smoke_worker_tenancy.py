"""Smoke tests for worker-tenancy (Task 20, ADR-0009 prep).

worker-tenancy is now a thin re-export of the platform tenant-context canon
(``worker_platform.context`` / ``worker_platform.presentation.middleware``) plus
the scope-based ``ClaimTenantResolver`` — the production tenant source that
reads ``tenant_id`` from ``request.state.user`` (set by the identity-service
auth middleware). The old UUID-typed ContextVars / ``HeaderTenantResolver`` /
``SubdomainTenantResolver`` / ``set_*`` helpers are gone (ADR-0005 left
``worker-middleware`` unsaid; ADR-0009 will record the cleanup).
"""

from __future__ import annotations

from types import SimpleNamespace
from uuid import UUID

import worker_platform.presentation.middleware as platform_mw
from worker_tenancy import (
    ClaimTenantResolver,
    DevelopmentHeaderTenantResolver,
    NoTenantResolver,
    TenantResolver,
)


def test_smoke_reexports_are_platform_canon() -> None:
    from worker_tenancy import NoTenantResolver as wt_no
    from worker_tenancy import TenantResolver as wt_tr

    assert wt_no is platform_mw.NoTenantResolver
    assert wt_tr is platform_mw.TenantResolver


def test_smoke_claim_resolver_reads_tenant_from_scope() -> None:
    principal = SimpleNamespace(tenant_id=UUID("11111111-1111-1111-1111-111111001111"))
    resolver = ClaimTenantResolver()
    assert resolver.resolve({"type": "http", "state": {"user": principal}}) == str(
        UUID("11111111-1111-1111-1111-111111001111")
    )


def test_smoke_default_resolvers_construct() -> None:
    NoTenantResolver()
    DevelopmentHeaderTenantResolver(enabled=False)


def test_smoke_tenant_resolver_protocol_satisfied() -> None:
    # The claim resolver satisfies the platform TenantResolver Protocol
    # (resolve(scope) -> str | None). Real integration is TenantContextMiddleware
    # wiring; here we assert the signature shape.
    resolver: TenantResolver = ClaimTenantResolver()
    assert resolver.resolve({"type": "http", "state": {}}) is None
