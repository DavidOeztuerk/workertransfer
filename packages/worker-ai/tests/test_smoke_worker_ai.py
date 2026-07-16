"""Smoke tests for worker-ai (Phase 1.5).

Exercises the lightweight surface only — dataclass construction of ``Message``.
The heavy provider clients (OpenAI/Anthropic/Ollama) are lazily imported inside
each provider's ``__init__`` and are intentionally NOT touched here (no network,
no API key, no model download).
"""

from worker_ai import Message


def test_smoke_message_constructs() -> None:
    message = Message(role="user", content="hello")

    assert message.role == "user"
    assert message.content == "hello"
