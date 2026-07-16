"""Smoke tests for worker-events (Phase 1.5).

Exercises the event dataclasses (``to_dict`` is pure), the in-process
``EventBus`` constructor, and an async publish → handler round-trip with a
no-op handler. No message broker / network is involved.
"""

from worker_events import DomainEvent, EventBus, IntegrationEvent


def test_smoke_domain_event_to_dict() -> None:
    event = DomainEvent(aggregate_type="Sample", version=3)

    payload = event.to_dict()

    assert payload["event_type"] == "DomainEvent"
    assert payload["aggregate_type"] == "Sample"
    assert payload["version"] == 3
    assert payload["aggregate_id"] is None


def test_smoke_integration_event_extends_domain_event() -> None:
    integration = IntegrationEvent(aggregate_type="Sample")

    assert isinstance(integration, DomainEvent)
    assert integration.correlation_id is not None


def test_smoke_event_bus_subscribe_constructor() -> None:
    bus = EventBus()

    assert bus._handlers == {}


async def test_smoke_event_bus_publish_invokes_handler() -> None:
    bus = EventBus()
    seen: list[DomainEvent] = []

    async def handler(event: DomainEvent) -> None:
        seen.append(event)

    bus.subscribe(DomainEvent, handler)
    event = DomainEvent(aggregate_type="Sample")

    await bus.publish(event)

    assert seen == [event]
