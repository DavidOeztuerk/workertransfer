"""Message bus: RabbitMQ, Kafka, NATS, Serialization, Routing, Consumers, Publishers, Retry, DLQ."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from typing import Any, cast
from uuid import UUID, uuid4


@dataclass
class Message:
    message_id: UUID = field(default_factory=uuid4)
    topic: str = ""
    payload: dict[str, Any] = field(default_factory=dict)
    headers: dict[str, str] = field(default_factory=dict)
    timestamp: float = 0.0


class MessageBroker(ABC):
    @abstractmethod
    async def publish(self, topic: str, message: Message) -> None: ...

    @abstractmethod
    async def subscribe(self, topic: str, handler: Callable[..., Awaitable[None]]) -> None: ...

    @abstractmethod
    async def start(self) -> None: ...

    @abstractmethod
    async def stop(self) -> None: ...


class RabbitMQBroker(MessageBroker):
    def __init__(self, url: str) -> None:
        self._url = url
        self._connection: Any = None
        self._channel: Any = None

    async def start(self) -> None:
        import aio_pika

        self._connection = cast("Any", await aio_pika.connect_robust(self._url))
        self._channel = await self._connection.channel()

    async def publish(self, topic: str, message: Message) -> None:
        import json

        import aio_pika

        exchange = await self._channel.declare_exchange(topic, aio_pika.ExchangeType.TOPIC)
        await exchange.publish(
            aio_pika.Message(
                body=json.dumps(message.payload).encode(),
                headers=cast("Any", message.headers),
                message_id=str(message.message_id),
            ),
            routing_key=topic,
        )

    async def subscribe(self, topic: str, handler: Callable[..., Awaitable[None]]) -> None:
        import aio_pika

        queue = await self._channel.declare_queue(f"{topic}.queue", durable=True)
        exchange = await self._channel.declare_exchange(topic, aio_pika.ExchangeType.TOPIC)
        await queue.bind(exchange, routing_key=topic)
        await queue.consume(handler)

    async def stop(self) -> None:
        if self._connection is not None:
            await cast("Any", self._connection).close()


class KafkaBroker(MessageBroker):
    def __init__(self, bootstrap_servers: str) -> None:
        self._bootstrap_servers = bootstrap_servers
        self._producer: Any = None
        self._consumer_task: Any = None

    async def start(self) -> None:
        from aiokafka import AIOKafkaProducer

        self._producer = AIOKafkaProducer(bootstrap_servers=self._bootstrap_servers)
        await cast("Any", self._producer).start()

    async def publish(self, topic: str, message: Message) -> None:
        import json

        await cast("Any", self._producer).send_and_wait(topic, json.dumps(message.payload).encode())

    async def subscribe(self, topic: str, handler: Callable[..., Awaitable[None]]) -> None:
        from aiokafka import AIOKafkaConsumer

        consumer = AIOKafkaConsumer(topic, bootstrap_servers=self._bootstrap_servers)
        await cast("Any", consumer).start()
        # Handle messages in background
        import asyncio

        self._consumer_task = asyncio.create_task(self._consume(cast("Any", consumer), handler))

    async def _consume(self, consumer: Any, handler: Callable[..., Awaitable[None]]) -> None:
        async for msg in consumer:
            await handler(cast("Any", msg))

    async def stop(self) -> None:
        await cast("Any", self._producer).stop()


class NATSBroker(MessageBroker):
    def __init__(self, servers: list[str]) -> None:
        self._servers = servers
        self._nc: Any = None

    async def start(self) -> None:
        import nats

        self._nc = cast("Any", await nats.connect(servers=self._servers))

    async def publish(self, topic: str, message: Message) -> None:
        import json

        await cast("Any", self._nc).publish(topic, json.dumps(message.payload).encode())

    async def subscribe(self, topic: str, handler: Callable[..., Awaitable[None]]) -> None:
        await cast("Any", self._nc).subscribe(topic, cb=cast("Any", handler))

    async def stop(self) -> None:
        await cast("Any", self._nc).close()
