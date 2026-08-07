"""HTTP problem details and exception mappings."""

from __future__ import annotations

import logging
from typing import Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from worker_platform.context import get_correlation_id

_logger = logging.getLogger("workertransfer.presentation.errors")


def _problem(
    *,
    status: int,
    title: str,
    detail: str,
    correlation_id: str | None,
    errors: list[Any] | None = None,
) -> JSONResponse:
    body: dict[str, Any] = {
        "type": f"https://workertransfer.dev/problems/{status}",
        "title": title,
        "status": status,
        "detail": detail,
    }
    if correlation_id is not None:
        body["correlationId"] = correlation_id
    if errors:
        body["errors"] = errors
    return JSONResponse(status_code=status, content=body, media_type="application/problem+json")


def register_exception_handlers(app: FastAPI) -> None:
    @app.exception_handler(RequestValidationError)
    async def request_validation_error_handler(
        _: Request, exception: RequestValidationError
    ) -> JSONResponse:
        return _problem(
            status=422,
            title="Request validation failed",
            detail="One or more request fields are invalid.",
            correlation_id=get_correlation_id(),
            errors=list(exception.errors()),
        )

    @app.exception_handler(HTTPException)
    async def http_exception_handler(_: Request, exception: HTTPException) -> JSONResponse:
        detail = exception.detail if isinstance(exception.detail, str) else "The request failed."
        return _problem(
            status=exception.status_code,
            title="Request failed",
            detail=detail,
            correlation_id=get_correlation_id(),
        )

    @app.exception_handler(Exception)
    async def unhandled_exception_handler(_: Request, exception: Exception) -> JSONResponse:
        _logger.exception("unhandled_request_exception", exc_info=exception)
        return _problem(
            status=500,
            title="Internal server error",
            detail="An unexpected error occurred.",
            correlation_id=get_correlation_id(),
        )
