"""Resilience patterns: Retry, Circuit Breaker, Timeout, Bulkhead, Fallback."""

from __future__ import annotations

import asyncio
import logging
from collections.abc import Awaitable, Callable
from functools import wraps
from typing import Any, TypeVar, cast

from tenacity import (
    after_log,
    before_sleep_log,
    retry,
    retry_if_exception_type,
    stop_after_attempt,
    wait_exponential,
)

logger = logging.getLogger(__name__)

T = TypeVar("T")
ExceptionTypes = tuple[type[BaseException], ...]


def with_retry(
    max_attempts: int = 3,
    base_delay: float = 1.0,
    max_delay: float = 60.0,
    exceptions: ExceptionTypes = (Exception,),
) -> Callable[[Callable[..., T]], Callable[..., T]]:
    def decorator(func: Callable[..., T]) -> Callable[..., T]:
        @wraps(func)
        @retry(
            stop=stop_after_attempt(max_attempts),
            wait=wait_exponential(multiplier=base_delay, max=max_delay),
            retry=retry_if_exception_type(exceptions),
            before_sleep=before_sleep_log(logger, logging.WARNING),
            after=after_log(logger, logging.INFO),
        )
        async def async_wrapper(*args: Any, **kwargs: Any) -> T:
            return await cast(Awaitable[T], func(*args, **kwargs))

        @wraps(func)
        @retry(
            stop=stop_after_attempt(max_attempts),
            wait=wait_exponential(multiplier=base_delay, max=max_delay),
            retry=retry_if_exception_type(exceptions),
            before_sleep=before_sleep_log(logger, logging.WARNING),
            after=after_log(logger, logging.INFO),
        )
        def sync_wrapper(*args: Any, **kwargs: Any) -> T:
            return func(*args, **kwargs)

        if asyncio.iscoroutinefunction(func):
            return cast("Callable[..., T]", async_wrapper)
        return sync_wrapper

    return decorator


class CircuitBreaker:
    def __init__(
        self,
        failure_threshold: int = 5,
        recovery_timeout: float = 30.0,
        expected_exception: type[BaseException] = Exception,
    ) -> None:
        self.failure_threshold = failure_threshold
        self.recovery_timeout = recovery_timeout
        self.expected_exception = expected_exception
        self._failures = 0
        self._last_failure_time: float | None = None
        self._state = "closed"  # closed, open, half-open

    @property
    def state(self) -> str:
        if self._state == "open":
            if (
                self._last_failure_time
                and (asyncio.get_event_loop().time() - self._last_failure_time)
                > self.recovery_timeout
            ):
                self._state = "half-open"
        return self._state

    async def call(self, func: Callable[..., T], *args: Any, **kwargs: Any) -> T:
        if self.state == "open":
            raise CircuitOpenError("Circuit breaker is open")

        try:
            if asyncio.iscoroutinefunction(func):
                result = await cast(Awaitable[T], func(*args, **kwargs))
            else:
                result = func(*args, **kwargs)

            if self._state == "half-open":
                self._state = "closed"
                self._failures = 0
            return result
        except self.expected_exception:
            self._failures += 1
            self._last_failure_time = asyncio.get_event_loop().time()
            if self._failures >= self.failure_threshold:
                self._state = "open"
            raise


class CircuitOpenError(Exception):
    pass


class Bulkhead:
    def __init__(self, max_concurrent: int = 10) -> None:
        self._semaphore = asyncio.Semaphore(max_concurrent)

    async def execute(self, func: Callable[..., T], *args: Any, **kwargs: Any) -> T:
        async with self._semaphore:
            if asyncio.iscoroutinefunction(func):
                return await cast(Awaitable[T], func(*args, **kwargs))
            return func(*args, **kwargs)


def timeout(seconds: float) -> Callable[[Callable[..., T]], Callable[..., T]]:
    def decorator(func: Callable[..., T]) -> Callable[..., T]:
        @wraps(func)
        async def async_wrapper(*args: Any, **kwargs: Any) -> T:
            awaitable = cast(Awaitable[T], func(*args, **kwargs))
            return await asyncio.wait_for(awaitable, timeout=seconds)

        @wraps(func)
        def sync_wrapper(*args: Any, **kwargs: Any) -> T:
            raise RuntimeError(
                "timeout() decorator targets async callables; sync functions are not supported."
            )

        if asyncio.iscoroutinefunction(func):
            return cast("Callable[..., T]", async_wrapper)
        return sync_wrapper

    return decorator


def fallback[T](fallback_func: Callable[..., T]) -> Callable[[Callable[..., T]], Callable[..., T]]:
    def decorator(func: Callable[..., T]) -> Callable[..., T]:
        @wraps(func)
        async def async_wrapper(*args: Any, **kwargs: Any) -> T:
            try:
                return await cast(Awaitable[T], func(*args, **kwargs))
            except Exception:
                return fallback_func(*args, **kwargs)

        @wraps(func)
        def sync_wrapper(*args: Any, **kwargs: Any) -> T:
            try:
                return func(*args, **kwargs)
            except Exception:
                return fallback_func(*args, **kwargs)

        if asyncio.iscoroutinefunction(func):
            return cast("Callable[..., T]", async_wrapper)
        return sync_wrapper

    return decorator
