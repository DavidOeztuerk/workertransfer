"""RFC 9457 ProblemDetails, exception mapping, and error codes."""

from typing import Any

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from starlette import status


class ProblemDetail(BaseModel):
    type: str = "about:blank"
    title: str
    status: int
    detail: str | None = None
    instance: str | None = None
    extensions: dict[str, Any] = {}


def to_problem_detail(exc: Exception, path: str) -> ProblemDetail:
    if isinstance(exc, ValidationError):
        return ProblemDetail(
            title="Validation Error",
            status=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=str(exc),
            instance=path,
        )
    if isinstance(exc, AuthenticationError):
        return ProblemDetail(
            title="Unauthorized",
            status=status.HTTP_401_UNAUTHORIZED,
            detail=str(exc),
            instance=path,
        )
    if isinstance(exc, AuthorizationError):
        return ProblemDetail(
            title="Forbidden",
            status=status.HTTP_403_FORBIDDEN,
            detail=str(exc),
            instance=path,
        )
    if isinstance(exc, NotFoundError):
        return ProblemDetail(
            title="Not Found",
            status=status.HTTP_404_NOT_FOUND,
            detail=str(exc),
            instance=path,
        )
    if isinstance(exc, ConflictError):
        return ProblemDetail(
            title="Conflict",
            status=status.HTTP_409_CONFLICT,
            detail=str(exc),
            instance=path,
        )
    if isinstance(exc, RateLimitError):
        return ProblemDetail(
            title="Too Many Requests",
            status=status.HTTP_429_TOO_MANY_REQUESTS,
            detail=str(exc),
            instance=path,
        )

    return ProblemDetail(
        title="Internal Server Error",
        status=status.HTTP_500_INTERNAL_SERVER_ERROR,
        detail="An unexpected error occurred",
        instance=path,
    )


def register_exception_handlers(app: FastAPI) -> None:
    @app.exception_handler(Exception)
    async def handle_exception(request: Request, exc: Exception) -> JSONResponse:
        problem = to_problem_detail(exc, str(request.url))
        return JSONResponse(
            status_code=problem.status,
            content=problem.model_dump(),
            media_type="application/problem+json",
        )


class ValidationError(Exception):
    pass


class AuthenticationError(Exception):
    pass


class AuthorizationError(Exception):
    pass


class NotFoundError(Exception):
    pass


class ConflictError(Exception):
    pass


class RateLimitError(Exception):
    pass
