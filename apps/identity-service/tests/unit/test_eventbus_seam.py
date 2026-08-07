"""Task 22: EventBus subscription seam — compose wires production handlers.

The Task-17 commands already publish ``UserLoggedIn``/``UserRegistered`` on the
in-process ``EventBus`` after a UoW commit. Task 22 *wires the subscription
seam*: production no-op handlers for those domain events so future side-effect
handlers (notifications, etc.) have a place to hook in. Audit persistence
itself stays synchronous inside the command's UoW (ADR-0012) — audit is NOT
republished through the EventBus.

This is a unit test (no DB container): it injects a ``RecordingEventBus`` into
``compose_infrastructure`` and asserts the production wiring subscribed a
handler for both ``UserLoggedIn`` and ``UserRegistered``.
"""

from __future__ import annotations

from collections.abc import Awaitable, Callable

from identity_service.configuration import IdentityServiceSettings
from identity_service.infrastructure.compose import compose_infrastructure
from worker_events import EventBus


class RecordingEventBus(EventBus):
    """EventBus that records every ``subscribe`` call by event_type name."""

    def __init__(self) -> None:
        super().__init__()
        self.subscriptions: list[tuple[str, Callable[..., Awaitable[None]]]] = []

    def subscribe(self, event_type: type, handler: Callable[..., Awaitable[None]]) -> None:  # type: ignore[override]
        self.subscriptions.append((event_type.__name__, handler))
        super().subscribe(event_type, handler)  # type:ignore[arg-type]


def test_compose_infrastructure_subscribes_handlers_for_domain_events() -> None:
    settings = IdentityServiceSettings()
    # engine is lazy-constructed; an unreachable DB string is fine — we never
    # open a connection; we only assert the EventBus wiring.
    from sqlalchemy.ext.asyncio import create_async_engine

    engine = create_async_engine("postgresql+asyncpg://nobody@nowhere/x")
    bus = RecordingEventBus()

    deps = compose_infrastructure(settings, engine, eventbus=bus)

    assert deps["eventbus"] is bus
    subscribed = {name for name, _handler in bus.subscriptions}
    assert "UserLoggedIn" in subscribed
    assert "UserRegistered" in subscribed
