from dataclasses import dataclass

import pytest
from worker_platform.application.cqrs import (
    Command,
    HandlerNotRegisteredError,
    Mediator,
    PipelineBehavior,
    Request,
)


@dataclass
class AddNumbers(Command[int]):
    left: int
    right: int


class RecordingBehavior(PipelineBehavior):
    def __init__(self, events: list[str]) -> None:
        self.events = events

    async def handle(self, request: Request[object], next_handler):  # type: ignore[no-untyped-def]
        self.events.append("before")
        result = await next_handler(request)
        self.events.append("after")
        return result


@pytest.mark.asyncio
async def test_mediator_executes_ordered_pipeline() -> None:
    mediator = Mediator()
    events: list[str] = []

    async def handle(command: AddNumbers) -> int:
        events.append("handler")
        return command.left + command.right

    mediator.register_handler(AddNumbers, handle)
    mediator.add_behavior(RecordingBehavior(events))

    assert await mediator.send(AddNumbers(20, 22)) == 42
    assert events == ["before", "handler", "after"]


@pytest.mark.asyncio
async def test_mediator_rejects_unregistered_requests() -> None:
    mediator = Mediator()

    with pytest.raises(HandlerNotRegisteredError):
        await mediator.send(AddNumbers(1, 1))
