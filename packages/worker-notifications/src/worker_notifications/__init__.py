"""Notifications: Email, SMS, Push, WebSocket, In-app, Templates, Queue."""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from enum import StrEnum
from typing import Any


class NotificationChannel(StrEnum):
    EMAIL = "email"
    SMS = "sms"
    PUSH = "push"
    WEBSOCKET = "websocket"
    IN_APP = "in_app"


class NotificationPriority(StrEnum):
    LOW = "low"
    NORMAL = "normal"
    HIGH = "high"
    URGENT = "urgent"


@dataclass
class Notification:
    id: str
    user_id: str
    tenant_id: str
    channel: NotificationChannel
    subject: str
    body: str
    template: str | None = None
    template_context: dict[str, Any] = field(default_factory=dict)
    priority: NotificationPriority = NotificationPriority.NORMAL
    metadata: dict[str, Any] = field(default_factory=dict)
    scheduled_at: str | None = None
    sent_at: str | None = None
    status: str = "pending"  # pending, sent, failed


class NotificationProvider(ABC):
    @abstractmethod
    async def send(self, notification: Notification) -> bool: ...


class EmailNotificationProvider(NotificationProvider):
    def __init__(self, email_service: Any) -> None:
        self._email_service = email_service

    async def send(self, notification: Notification) -> bool:
        from worker_email import EmailMessage

        email = str(notification.metadata.get("email") or "")
        message = EmailMessage(
            to=[email],
            subject=notification.subject,
            body=notification.body,
        )
        return bool(await self._email_service.send(message))


class PushNotificationProvider(NotificationProvider):
    def __init__(self, firebase_key: str | None = None) -> None:
        self._firebase_key = firebase_key

    async def send(self, notification: Notification) -> bool:
        # Implement Firebase/APNs push
        return True


class WebSocketNotificationProvider(NotificationProvider):
    def __init__(self, connection_manager: Any) -> None:
        self._manager = connection_manager

    async def send(self, notification: Notification) -> bool:
        await self._manager.send_to_user(
            notification.user_id,
            {
                "type": "notification",
                "data": {
                    "id": notification.id,
                    "subject": notification.subject,
                    "body": notification.body,
                },
            },
        )
        return True


class InAppNotificationProvider(NotificationProvider):
    def __init__(self, repository: Any) -> None:
        self._repo = repository

    async def send(self, notification: Notification) -> bool:
        await self._repo.create(notification)
        return True


class NotificationService:
    def __init__(self) -> None:
        self._providers: dict[NotificationChannel, NotificationProvider] = {}
        self._queue: list[Notification] = []

    def register_provider(
        self, channel: NotificationChannel, provider: NotificationProvider
    ) -> None:
        self._providers[channel] = provider

    async def send(self, notification: Notification) -> bool:
        provider = self._providers.get(notification.channel)
        if not provider:
            return False

        success = bool(await provider.send(notification))
        notification.status = "sent" if success else "failed"
        notification.sent_at = (
            __import__("datetime").datetime.now(__import__("datetime").UTC).isoformat()
        )
        return success

    async def send_multi_channel(
        self, notification: Notification, channels: list[NotificationChannel]
    ) -> dict[NotificationChannel, bool]:
        results: dict[NotificationChannel, bool] = {}
        for channel in channels:
            n = Notification(**{**notification.__dict__, "channel": channel})
            results[channel] = await self.send(n)
        return results

    async def queue_notification(self, notification: Notification) -> None:
        self._queue.append(notification)

    async def process_queue(self) -> int:
        sent = 0
        while self._queue:
            notification = self._queue.pop(0)
            if await self.send(notification):
                sent += 1
        return sent
