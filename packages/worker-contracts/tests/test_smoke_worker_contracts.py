"""Smoke tests for worker-contracts (Phase 1.5).

Exercises the versioned boundary message types — ``Message`` / ``Command`` /
``Query`` / ``Event`` pydantic models. Construction is pure; ``frozen=True`` /
``extra="forbid"`` are verified as behavioral guarantees.
"""

from uuid import uuid4

import pytest
from pydantic import ValidationError
from worker_contracts import Command, Event, Message, Query


def test_smoke_message_constructs_frozen() -> None:
    message = Message(message_id=uuid4(), message_type="ping")

    assert message.message_type == "ping"
    assert message.correlation_id is None
    with pytest.raises(ValidationError):
        message.message_type = "pong"  # frozen=True


def test_smoke_message_rejects_extra_fields() -> None:
    with pytest.raises(ValidationError):
        Message(message_id=uuid4(), message_type="ping", surprise="nope")  # extra="forbid"


def test_smoke_command_query_event_subclass_message() -> None:
    command = Command(message_id=uuid4(), message_type="do")
    query = Query(message_id=uuid4(), message_type="ask")
    event = Event(message_id=uuid4(), message_type="happened")

    assert isinstance(command, Message)
    assert isinstance(query, Message)
    assert isinstance(event, Message)
