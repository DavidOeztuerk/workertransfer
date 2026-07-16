"""Context propagation for request correlation and tenant boundaries."""

from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager
from contextvars import ContextVar
from uuid import UUID, uuid4

_correlation_id: ContextVar[str | None] = ContextVar("correlation_id", default=None)
_tenant_id: ContextVar[str | None] = ContextVar("tenant_id", default=None)


def new_correlation_id() -> str:
    return str(uuid4())


def normalize_correlation_id(candidate: str | None) -> str:
    """Keep valid caller correlation IDs, replacing malformed values safely."""

    if candidate is not None:
        try:
            return str(UUID(candidate))
        except ValueError:
            pass
    return new_correlation_id()


def get_correlation_id() -> str | None:
    return _correlation_id.get()


def get_tenant_id() -> str | None:
    return _tenant_id.get()


@contextmanager
def correlation_context(correlation_id: str) -> Iterator[None]:
    token = _correlation_id.set(correlation_id)
    try:
        yield
    finally:
        _correlation_id.reset(token)


@contextmanager
def tenant_context(tenant_id: str | None) -> Iterator[None]:
    token = _tenant_id.set(tenant_id)
    try:
        yield
    finally:
        _tenant_id.reset(token)
