"""Smoke tests for worker-ai (Phase 1.5).

Exercises the lightweight surface only — dataclass construction of ``Message``.
The heavy provider clients (OpenAI/Anthropic/Ollama) are lazily imported inside
each provider's ``__init__`` and are intentionally NOT touched here (no network,
no API key, no model download).
"""

import pytest

try:
    from worker_ai import Message  # type: ignore[no-redef,unused-ignore]
except ImportError:
    pytest.skip("worker-ai is excluded from the workspace", allow_module_level=True)  # type: ignore[unused-ignore]


def test_smoke_message_constructs() -> None:
    message = Message(role="user", content="hello")

    assert message.role == "user"
    assert message.content == "hello"
