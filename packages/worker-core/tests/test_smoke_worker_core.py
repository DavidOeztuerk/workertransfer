"""Smoke tests for worker-core (Phase 1.5).

Exercises the domain kernel primitives — ``DomainError``, ``Entity`` identity
equality, ``Result.ok``/``Result.fail`` outcome handling, and ``DomainEvent``
metadata — all pure, no transport/ORM/framework dependency.
"""

from worker_core.domain import DomainError, DomainEvent, Entity, Result


def test_smoke_domain_error_carries_code_and_message() -> None:
    error = DomainError("E001", "boom")

    assert error.code == "E001"
    assert error.message == "boom"


def test_smoke_entity_equality_by_identity() -> None:
    class Sample(Entity):
        pass

    first = Sample()
    second = Sample()

    assert first == first
    assert first != second
    assert hash(first) == hash(first)


def test_smoke_result_ok_and_fail() -> None:
    success = Result.ok(value=42)

    assert success.is_success is True
    assert success.value == 42
    assert success.error is None

    failure = Result.fail(DomainError("E002", "nope"))

    assert failure.is_success is False
    assert failure.error is not None
    assert failure.error.code == "E002"


def test_smoke_domain_event_metadata() -> None:
    event = DomainEvent()

    assert event.event_id is not None
    assert event.occurred_at is not None
