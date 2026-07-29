"""Multi-tenancy: thin re-export of the platform tenant-context canon (ADR-0009 prep).

The canonical tenant contextvar lives in ``worker_platform.context`` (the
str-form of the UUID, per ADR-0002/ADR-0005). This keeps a thin re-export for
consumers and adds ``ClaimTenantResolver`` — the production tenant source that
derives the tenant id from ``request.state.user`` (set by the identity-service
auth middleware), returning the str-form for the platform str-typed contextvar.

Removed (recorded in ADR-0009): the UUID-typed ``_tenant_id``/``_tenant_context``
ContextVars, ``set_tenant_id``/``set_tenant_context``/``get_tenant_context``,
``HeaderTenantResolver`` (superseded by the platform
``DevelopmentHeaderTenantResolver`` for local/test), and the
``SubdomainTenantResolver`` stub (a real scope-based subdomain resolver is a
Phase-4 concern, re-added there). ``worker-middleware`` — the lone duplicate of
the platform canon with zero importers — was deleted alongside this
consolidation (the same treatment ADR-0005 gave ``worker-cqrs``).
"""

from __future__ import annotations

from starlette.types import Scope
from worker_platform.context import get_tenant_id, tenant_context
from worker_platform.presentation.middleware import (
    DevelopmentHeaderTenantResolver,
    NoTenantResolver,
    TenantResolver,
)

__all__ = [
    "ClaimTenantResolver",
    "DevelopmentHeaderTenantResolver",
    "NoTenantResolver",
    "TenantResolver",
    "get_tenant_id",
    "tenant_context",
]


class ClaimTenantResolver:
    """Production tenant source: read ``tenant_id`` from the authenticated
    principal attached at ``request.state.user`` by the auth middleware.

    Returns the str-form of the tenant id (UUID_str) for the platform
    str-typed tenant contextvar; ``None`` when there is no user, no
    tenant_id, or the attribute is absent.
    """

    def __init__(self, claim_attr: str = "tenant_id") -> None:
        self.claim_attr = claim_attr

    def resolve(self, scope: Scope) -> str | None:
        state = scope.get("state") or {}
        user = state.get("user")
        if user is None:
            return None
        value = getattr(user, self.claim_attr, None)
        if value is None:
            return None
        return str(value)
