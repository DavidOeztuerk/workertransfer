"""Smoke tests for worker-resilience (Phase 1.5).

Exercises the ``CircuitBreaker`` constructor (scalar fields + ``_state="closed"``,
``_failures=0``) and the ``with_retry`` decorator factory (returns a callable;
does not execute). ``cb.call(func)`` is NOT called — it runs ``func`` and
mutates state. The ``.state`` property touches the event loop and is avoided.
"""

from worker_resilience import CircuitBreaker, with_retry


def test_smoke_circuit_breaker_and_retry_factory() -> None:
    breaker = CircuitBreaker(failure_threshold=3)
    decorator = with_retry(max_attempts=1)

    assert breaker.failure_threshold == 3
    assert breaker._state == "closed"
    assert breaker._failures == 0
    assert callable(decorator)
