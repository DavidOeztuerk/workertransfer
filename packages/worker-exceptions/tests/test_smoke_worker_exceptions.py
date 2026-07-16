"""Smoke tests for worker-exceptions (Phase 1.5).

Exercises the pure RFC 9457 surface — ``ProblemDetail`` model construction and
``to_problem_detail`` mapping for the different exception classes (pure
function: exception + path → ``ProblemDetail``, no FastAPI app, no request).
``register_exception_handlers`` needs a FastAPI app and is not exercised here.
"""

from worker_exceptions import (
    AuthenticationError,
    NotFoundError,
    ProblemDetail,
    ValidationError,
    to_problem_detail,
)


def test_smoke_problem_detail_defaults() -> None:
    detail = ProblemDetail(title="Internal Server Error", status=500)

    assert detail.type == "about:blank"
    assert detail.title == "Internal Server Error"
    assert detail.status == 500
    assert detail.detail is None


def test_smoke_to_problem_detail_maps_known_errors() -> None:
    validation = to_problem_detail(ValidationError("bad input"), "/items")
    not_found = to_problem_detail(NotFoundError("missing"), "/items/1")
    auth = to_problem_detail(AuthenticationError("no token"), "/")

    assert validation.status == 422
    assert validation.detail == "bad input"
    assert not_found.status == 404
    assert auth.status == 401


def test_smoke_to_problem_detail_unknown_error_is_500() -> None:
    detail = to_problem_detail(RuntimeError("oops"), "/x")

    assert detail.status == 500
    assert detail.title == "Internal Server Error"
