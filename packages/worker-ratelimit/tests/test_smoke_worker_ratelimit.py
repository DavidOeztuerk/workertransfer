"""Smoke test for worker-ratelimit (Phase 1.5).

``RateLimiter``/``SlidingWindowRateLimiter`` constructors allocate a real Redis
client via ``redis.from_url``; ``check_limit`` / ``get_limit_info`` hit Redis.
There is no pure helper, so the smoke stays at module-import level (import is
verified clean — ``redis.asyncio`` loads with no connection).
"""

import worker_ratelimit


def test_smoke_module_imports() -> None:
    assert worker_ratelimit is not None
    names = {n for n in dir(worker_ratelimit) if not n.startswith("_")}
    assert {"RateLimiter", "SlidingWindowRateLimiter"} <= names
