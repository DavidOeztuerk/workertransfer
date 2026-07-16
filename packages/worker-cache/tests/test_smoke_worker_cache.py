"""Smoke tests for worker-cache (Phase 1.5).

Exercises the in-memory backend constructor and the ``cached`` decorator
factory (pure; returns a callable). ``MemoryCache.get/set`` are async and would
need ``await``; the ctor + factory are sync and network-free. ``redis`` is
lazily imported inside ``RedisCache._ensure_connected``.
"""

from worker_cache import MemoryCache, cached


def test_smoke_memory_cache_and_decorator() -> None:
    backend = MemoryCache()
    decorator = cached(backend, ttl=10, key_prefix="k:")

    assert backend._store == {}
    assert callable(decorator)


async def test_smoke_memory_cache_set_get() -> None:
    memory = MemoryCache()

    await memory.set("k", "v", ttl=60)
    value = await memory.get("k")

    assert value == "v"
