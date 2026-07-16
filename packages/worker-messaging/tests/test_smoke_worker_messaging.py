"""Smoke tests for worker-messaging (Phase 1.5).

Exercises the ``Message`` dataclass (uuid default factory) and the
``RabbitMQBroker`` constructor (stores url only, no connection). ``start()``
is NOT called — it would open a real AMQP connection. ``aio_pika`` is lazily
imported there, so the smoke stays network-free.
"""

from worker_messaging import Message, RabbitMQBroker


def test_smoke_message_and_broker() -> None:
    message = Message(topic="t")

    assert message.topic == "t"
    assert message.message_id is not None

    broker = RabbitMQBroker("amqp://guest:guest@localhost")

    assert broker._url == "amqp://guest:guest@localhost"
    assert broker._connection is None


def test_smoke_kafka_and_nats_constructors() -> None:
    from worker_messaging import KafkaBroker, NATSBroker

    kafka = KafkaBroker("localhost:9092")
    nats = NATSBroker(["nats://localhost:4222"])

    assert kafka._bootstrap_servers == "localhost:9092"
    assert kafka._producer is None
    assert nats._servers == ["nats://localhost:4222"]
    assert nats._nc is None
