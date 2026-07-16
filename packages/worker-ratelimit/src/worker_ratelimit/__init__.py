"""Rate limiting: Token bucket, Sliding window, Distributed (Redis)."""

import time
from typing import Any, cast

import redis.asyncio as redis


class RateLimiter:
    def __init__(self, redis_url: str = "redis://localhost:6379/0"):
        self._redis = redis.from_url(redis_url, decode_responses=True)  # type: ignore[no-untyped-call]

    async def check_limit(self, key: str, limit: int, window: int) -> tuple[bool, dict[str, Any]]:
        """Token bucket algorithm with Redis."""
        now = time.time()
        pipeline = self._redis.pipeline()

        # Remove expired tokens
        pipeline.zremrangebyscore(key, 0, now - window)

        # Count current tokens
        pipeline.zcard(key)

        # Add current request
        pipeline.zadd(key, {f"{now}": now})
        pipeline.expire(key, window + 1)

        results = cast("list[Any]", await pipeline.execute())
        current_count = int(results[1])

        allowed = current_count < limit
        remaining = max(0, limit - current_count - 1)

        return allowed, {
            "limit": limit,
            "remaining": remaining,
            "reset": int(now + window),
        }

    async def get_limit_info(self, key: str, limit: int, window: int) -> dict[str, Any]:
        now = time.time()
        await self._redis.zremrangebyscore(key, 0, now - window)
        current = cast("int", await self._redis.zcard(key))
        return {
            "limit": limit,
            "remaining": max(0, limit - current),
            "reset": int(now + window),
        }


class SlidingWindowRateLimiter:
    def __init__(self, redis_url: str = "redis://localhost:6379/0"):
        self._redis = redis.from_url(redis_url, decode_responses=True)  # type: ignore[no-untyped-call]

    async def check_limit(self, key: str, limit: int, window: int) -> tuple[bool, dict[str, Any]]:
        now = time.time()
        window_start = now - window

        pipeline = self._redis.pipeline()
        pipeline.zremrangebyscore(key, 0, window_start)
        pipeline.zcard(key)
        pipeline.zadd(key, {f"{now}": now})
        pipeline.expire(key, window + 1)

        results = cast("list[Any]", await pipeline.execute())
        current_count = int(results[1])

        allowed = current_count < limit
        remaining = max(0, limit - current_count - 1)

        return allowed, {
            "limit": limit,
            "remaining": remaining,
            "reset": int(now + window),
        }
