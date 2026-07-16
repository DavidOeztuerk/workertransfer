"""Smoke tests for worker-notifications (Phase 1.5).

Exercises enums, the ``Notification`` dataclass default state, and the
``NotificationService``/``PushNotificationProvider`` constructors (empty
registry / stored key, no Firebase init). ``EmailNotificationProvider.send``
is NOT called — it touches a real email backend.
"""

from worker_notifications import (
    Notification,
    NotificationChannel,
    NotificationService,
    PushNotificationProvider,
)


def test_smoke_notification_service_and_defaults() -> None:
    service = NotificationService()
    provider = PushNotificationProvider()

    assert service._providers == {}
    assert service._queue == []
    assert provider._firebase_key is None

    notification = Notification(
        id="1",
        user_id="u",
        tenant_id="t",
        channel=NotificationChannel.IN_APP,
        subject="s",
        body="b",
    )

    assert notification.status == "pending"
    assert notification.priority.value == "normal"
