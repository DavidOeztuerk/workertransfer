"""Caching: Memory, Redis, Distributed, Response cache, CQRS cache, Decorators."""

from __future__ import annotations

import json
from abc import ABC, abstractmethod
from collections.abc import Awaitable, Callable
from functools import wraps
from typing import Any, cast


class CacheBackend(ABC):
    @abstractmethod
    async def get(self, key: str) -> Any | None: ...

    @abstractmethod
    async def set(self, key: str, value: Any, ttl: int = 3600) -> None: ...

    @abstractmethod
    async def delete(self, key: str) -> None: ...

    @abstractmethod
    async def exists(self, key: str) -> bool: ...


class MemoryCache(CacheBackend):
    def __init__(self) -> None:
        self._store: dict[str, tuple[Any, float]] = {}

    async def get(self, key: str) -> Any | None:
        import time

        if key in self._store:
            value, expires = self._store[key]
            if expires > time.time():
                return value
            del self._store[key]
        return None

    async def set(self, key: str, value: Any, ttl: int = 3600) -> None:
        import time

        self._store[key] = (value, time.time() + ttl)

    async def delete(self, key: str) -> None:
        self._store.pop(key, None)

    async def exists(self, key: str) -> bool:
        return await self.get(key) is not None


class RedisCache(CacheBackend):
    def __init__(self, redis_url: str) -> None:
        self._redis_url = redis_url
        self._redis: Any = None

    async def _ensure_connected(self) -> None:
        if self._redis is None:
            import redis.asyncio as redis

            self._redis = redis.from_url(self._redis_url)  # type: ignore[no-untyped-call]

    async def get(self, key: str) -> Any | None:
        await self._ensure_connected()
        value = cast("Any", await self._redis.get(key))
        return json.loads(value) if value else None

    async def set(self, key: str, value: Any, ttl: int = 3600) -> None:
        await self._ensure_connected()
        await self._redis.setex(key, ttl, json.dumps(value, default=str))

    async def delete(self, key: str) -> None:
        await self._ensure_connected()
        await self._redis.delete(key)

    async def exists(self, key: str) -> bool:
        await self._ensure_connected()
        return bool(await self._redis.exists(key)) > 0


def cached(
    backend: CacheBackend, ttl: int = 3600, key_prefix: str = ""
) -> Callable[[Callable[..., Awaitable[Any]]], Callable[..., Awaitable[Any]]]:
    def decorator(
        func: Callable[..., Awaitable[Any]],
    ) -> Callable[..., Awaitable[Any]]:
        @wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> Any:
            key = f"{key_prefix}{func.__name__}:{hash(str(args) + str(kwargs))}"
            cached_value = await backend.get(key)
            if cached_value is not None:
                return cached_value
            result = await func(*args, **kwargs)
            await backend.set(key, result, ttl)
            return result

        return wrapper

    return decorator
