"""Repository round-trip against a real Postgres.

The DISTINCT ON projection and the append-only guarantees cannot be verified in
memory — they are properties of the SQL and the constraints.
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from consent_service.domain.consent_event import ConsentEvent
from consent_service.domain.services import project_state
from consent_service.domain.value_objects import Capability, ConsentEventId, Reason, SubjectId
from consent_service.infrastructure.database.repositories import (
    SqlAlchemyConsentEventRepository,
)
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from ._docker import _docker_available

pytestmark = pytest.mark.skipif(not _docker_available(), reason="Docker not available")

BASE = datetime(2026, 7, 31, 12, 0, tzinfo=UTC)
CAPABILITY = Capability("profile.visibility:public")
WITHDRAWN = Reason("subject withdrew consent")


async def test_append_then_latest_effective_returns_the_fact(session: AsyncSession) -> None:
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())

    await repo.append(
        ConsentEvent.grant(subject_id=subject, capability=CAPABILITY, recorded_at=BASE)
    )

    latest = await repo.latest_effective(subject, CAPABILITY)
    assert latest is not None
    assert project_state([latest]).granted is True


async def test_revoke_is_immediately_visible(session: AsyncSession) -> None:
    """product-scope.md: revocation must withdraw the capability immediately."""
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())

    await repo.append(
        ConsentEvent.grant(subject_id=subject, capability=CAPABILITY, recorded_at=BASE)
    )
    granted = await repo.latest_effective(subject, CAPABILITY)
    assert granted is not None and project_state([granted]).granted is True

    await repo.append(
        ConsentEvent.revoke(
            subject_id=subject,
            capability=CAPABILITY,
            recorded_at=BASE + timedelta(seconds=1),
            reason=WITHDRAWN,
        )
    )

    # No cache anywhere: the very next read reflects the withdrawal.
    revoked = await repo.latest_effective(subject, CAPABILITY)
    assert revoked is not None
    state = project_state([revoked])
    assert state.granted is False
    assert state.reason == WITHDRAWN.value


async def test_re_consent_after_revoke(session: AsyncSession) -> None:
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())

    for offset, factory in ((0, "grant"), (1, "revoke"), (2, "grant")):
        if factory == "grant":
            event = ConsentEvent.grant(
                subject_id=subject,
                capability=CAPABILITY,
                recorded_at=BASE + timedelta(seconds=offset),
            )
        else:
            event = ConsentEvent.revoke(
                subject_id=subject,
                capability=CAPABILITY,
                recorded_at=BASE + timedelta(seconds=offset),
                reason=WITHDRAWN,
            )
        await repo.append(event)

    latest = await repo.latest_effective(subject, CAPABILITY)
    assert latest is not None
    assert project_state([latest]).granted is True


async def test_capabilities_are_isolated_from_each_other(session: AsyncSession) -> None:
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())
    other = Capability("document.attach:application")

    await repo.append(
        ConsentEvent.grant(subject_id=subject, capability=CAPABILITY, recorded_at=BASE)
    )
    await repo.append(
        ConsentEvent.revoke(
            subject_id=subject, capability=other, recorded_at=BASE, reason=WITHDRAWN
        )
    )

    # Consent for one purpose must never authorise another (consent-ledger skill).
    first = await repo.latest_effective(subject, CAPABILITY)
    second = await repo.latest_effective(subject, other)
    assert first is not None and project_state([first]).granted is True
    assert second is not None and project_state([second]).granted is False


async def test_unknown_pair_has_no_fact(session: AsyncSession) -> None:
    repo = SqlAlchemyConsentEventRepository(session)
    assert await repo.latest_effective(SubjectId(uuid4()), CAPABILITY) is None


async def test_duplicate_event_id_is_rejected(session: AsyncSession) -> None:
    """event_id is the idempotency key: a replayed write cannot double-record."""
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())
    event_id = ConsentEventId(uuid4())

    await repo.append(
        ConsentEvent.grant(
            subject_id=subject, capability=CAPABILITY, recorded_at=BASE, event_id=event_id
        )
    )
    with pytest.raises(IntegrityError):
        await repo.append(
            ConsentEvent.grant(
                subject_id=subject,
                capability=CAPABILITY,
                recorded_at=BASE + timedelta(seconds=1),
                event_id=event_id,
            )
        )


async def test_stream_returns_every_fact_oldest_first(session: AsyncSession) -> None:
    repo = SqlAlchemyConsentEventRepository(session)
    subject = SubjectId(uuid4())

    await repo.append(
        ConsentEvent.grant(subject_id=subject, capability=CAPABILITY, recorded_at=BASE)
    )
    await repo.append(
        ConsentEvent.revoke(
            subject_id=subject,
            capability=CAPABILITY,
            recorded_at=BASE + timedelta(seconds=1),
            reason=WITHDRAWN,
        )
    )

    events = await repo.stream(subject)
    assert [e.action.value for e in events] == ["GRANT", "REVOKE"]
    # History is retained after a revoke — the ledger is the audit trail.
    assert len(events) == 2


async def test_repository_offers_no_mutation_api() -> None:
    """Append-only is structural, not a convention."""
    assert not hasattr(SqlAlchemyConsentEventRepository, "update")
    assert not hasattr(SqlAlchemyConsentEventRepository, "delete")
