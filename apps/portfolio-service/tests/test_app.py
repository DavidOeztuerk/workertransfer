"""App-Verdrahtung: Routen, Verträge und die Zugangsregeln."""

from __future__ import annotations

from typing import Any
from uuid import UUID

import pytest
from fastapi.testclient import TestClient
from portfolio_service.configuration import PortfolioServiceSettings
from portfolio_service.main import create_app


def _client() -> TestClient:
    return TestClient(create_app(PortfolioServiceSettings()))


def _schema() -> dict[str, Any]:
    return create_app(PortfolioServiceSettings()).openapi()


def test_liveness_reports_this_service() -> None:
    response = _client().get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "portfolio-service"}
    assert str(UUID(response.headers["x-correlation-id"])) == response.headers["x-correlation-id"]
    assert response.headers["x-content-type-options"] == "nosniff"


def test_the_three_routes_exist() -> None:
    paths = _schema()["paths"]

    assert set(paths["/portfolios/me"]) == {"put", "get"}
    assert "/portfolios/{subject_id}" in paths


def test_the_save_body_carries_no_visibility_flag() -> None:
    """Wer das Portfolio sehen darf, steht im Ledger — nicht im Körper."""
    schema = _schema()
    ref = schema["paths"]["/portfolios/me"]["put"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"items"}


@pytest.mark.parametrize(
    ("method", "path"),
    [
        ("put", "/portfolios/me"),
        ("get", "/portfolios/me"),
        ("get", "/portfolios/11111111-1111-1111-1111-111111111111"),
    ],
)
def test_every_route_requires_authentication(method: str, path: str) -> None:
    response = _client().request(method, path, json={"items": []})

    assert response.status_code == 401
