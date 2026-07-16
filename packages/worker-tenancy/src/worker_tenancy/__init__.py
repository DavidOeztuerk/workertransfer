"""Multi-tenancy: Tenant resolution, Context, Isolation strategies."""

from __future__ import annotations

from contextvars import ContextVar
from typing import Any
from uuid import UUID

from starlette.requests import Request

__all__ = [
    "ClaimTenantResolver",
    "HeaderTenantResolver",
    "SubdomainTenantResolver",
    "TenantResolver",
    "get_tenant_context",
    "get_tenant_id",
    "set_tenant_context",
    "set_tenant_id",
]

_tenant_id: ContextVar[UUID | None] = ContextVar("tenant_id", default=None)
_tenant_context: ContextVar[dict[str, Any] | None] = ContextVar("tenant_context", default=None)


def set_tenant_id(tenant_id: UUID | None) -> None:
    _tenant_id.set(tenant_id)


def get_tenant_id() -> UUID | None:
    return _tenant_id.get()


def set_tenant_context(context: dict[str, Any] | None) -> None:
    _tenant_context.set(context)


def get_tenant_context() -> dict[str, Any] | None:
    return _tenant_context.get()


class TenantResolver:
    def resolve(self, request: Request) -> UUID | None:
        # Override in subclasses
        return None


class HeaderTenantResolver(TenantResolver):
    def __init__(self, header_name: str = "X-Tenant-ID") -> None:
        self.header_name = header_name

    def resolve(self, request: Request) -> UUID | None:
        value = request.headers.get(self.header_name)
        if value:
            try:
                return UUID(value)
            except ValueError:
                pass
        return None


class ClaimTenantResolver(TenantResolver):
    def __init__(self, claim: str = "tenant_id") -> None:
        self.claim = claim

    def resolve(self, request: Request) -> UUID | None:
        if hasattr(request.state, "user"):
            tenant_id: Any = getattr(request.state.user, self.claim, None)
            if tenant_id:
                return UUID(str(tenant_id))
        return None


class SubdomainTenantResolver(TenantResolver):
    def resolve(self, request: Request) -> UUID | None:
        host = request.headers.get("host", "")
        # Phase 4 will implement tenant-by-subdomain lookup; the parse is kept here as a stub.
        subdomain = host.split(".")[0]  # noqa: F841
        # Look up tenant by subdomain
        return None  # Implement lookup


class NoTenantResolver(TenantResolver):
    """Resolver that never resolves a tenant (open-context default).

    Used in local/dev/test for routes that are intentionally tenant-agnostic.
    """

    def resolve(self, request: Request) -> UUID | None:
        return None
