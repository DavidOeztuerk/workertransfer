"""The audit row records the tenant the auth middleware resolved (ADR-0009/0012).

These run without Docker on purpose. The tenant used to be hardcoded to None
while the column existed, was indexed, and identity-service filled it correctly —
a gap no integration test noticed because none of them asserted on it.
"""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any
from uuid import uuid4

import pytest
from consent_service.application.commands import (
    GrantConsentCommand,
    RevokeConsentCommand,
    handle_grant,
    handle_revoke,
)
from consent_service.domain.consent_event import ConsentEvent
from worker_platform.context import tenant_context

_CAPABILITY = "profile.visibility:public"


class _FakeClock:
    def now(self) -> datetime:
        return datetime(2026, 7, 31, 12, 0, tzinfo=UTC)


class _FakeConsentRepo:
    def __init__(self) -> None:
        self.appended: list[ConsentEvent] = []

    async def append(self, event: ConsentEvent) -> None:
        self.appended.append(event)

    async def latest_effective(self, subject_id: Any, capability: Any) -> ConsentEvent | None:
        return self.appended[-1] if self.appended else None


class _FakeAuditRepo:
    def __init__(self) -> None:
        self.appended: list[Any] = []

    async def append(self, event: Any) -> None:
        self.appended.append(event)


@pytest.fixture
def repos() -> dict[str, Any]:
    return {"consent": _FakeConsentRepo(), "audit": _FakeAuditRepo()}


@pytest.fixture
def deps() -> dict[str, Any]:
    return {"clock": _FakeClock()}


async def test_audit_row_carries_the_tenant_from_context(
    repos: dict[str, Any], deps: dict[str, Any]
) -> None:
    subject = uuid4()
    tenant = uuid4()
    command = GrantConsentCommand(subject_id=subject, capability=_CAPABILITY, actor_id=subject)

    with tenant_context(str(tenant)):
        result = await handle_grant(command, deps=deps, repos=repos)

    assert result.is_success
    assert repos["audit"].appended[0].tenant_id == tenant


async def test_revoke_is_attributed_too(repos: dict[str, Any], deps: dict[str, Any]) -> None:
    subject = uuid4()
    tenant = uuid4()
    command = RevokeConsentCommand(
        subject_id=subject, capability=_CAPABILITY, actor_id=subject, reason="changed my mind"
    )

    with tenant_context(str(tenant)):
        result = await handle_revoke(command, deps=deps, repos=repos)

    assert result.is_success
    assert repos["audit"].appended[0].tenant_id == tenant


async def test_no_tenant_outside_a_request_scope(
    repos: dict[str, Any], deps: dict[str, Any]
) -> None:
    # CLI and test callers have no request context; None is the honest answer.
    subject = uuid4()
    command = GrantConsentCommand(subject_id=subject, capability=_CAPABILITY, actor_id=subject)

    result = await handle_grant(command, deps=deps, repos=repos)

    assert result.is_success
    assert repos["audit"].appended[0].tenant_id is None


async def test_a_malformed_tenant_claim_does_not_lose_the_consent_fact(
    repos: dict[str, Any], deps: dict[str, Any]
) -> None:
    # Losing the attribution is bad; refusing to record the consent itself is worse.
    subject = uuid4()
    command = GrantConsentCommand(subject_id=subject, capability=_CAPABILITY, actor_id=subject)

    with tenant_context("not-a-uuid"):
        result = await handle_grant(command, deps=deps, repos=repos)

    assert result.is_success
    assert repos["audit"].appended[0].tenant_id is None
    assert len(repos["consent"].appended) == 1
