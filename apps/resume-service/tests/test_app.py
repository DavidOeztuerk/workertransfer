"""App-Verdrahtung: Routen, Verträge und die Zugangsregeln."""

from __future__ import annotations

from typing import Any
from uuid import UUID

import pytest
from fastapi.testclient import TestClient
from resume_service.configuration import ResumeServiceSettings
from resume_service.main import create_app


def _client() -> TestClient:
    return TestClient(create_app(ResumeServiceSettings()))


def _schema() -> dict[str, Any]:
    return create_app(ResumeServiceSettings()).openapi()


def test_liveness_reports_this_service() -> None:
    response = _client().get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "resume-service"}
    assert str(UUID(response.headers["x-correlation-id"])) == response.headers["x-correlation-id"]
    assert response.headers["x-content-type-options"] == "nosniff"


def test_the_own_resume_routes_exist() -> None:
    paths = _schema()["paths"]

    assert "/resumes/me" in paths
    assert set(paths["/resumes/me"]) == {"put", "get"}


def test_the_save_body_names_no_recipient_and_no_visibility() -> None:
    """Wer den Lebenslauf sehen darf, steht im Ledger — nicht im Körper."""
    schema = _schema()
    ref = schema["paths"]["/resumes/me"]["put"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"positions", "education"}


def test_months_are_pinned_to_the_wire_format() -> None:
    # Das Muster ist Teil des Vertrags: ein Client, der Tagesdaten schickt, soll
    # eine klare Ablehnung bekommen und nicht eine stille Umdeutung.
    schema = _schema()["components"]["schemas"]["PositionV1"]["properties"]

    assert schema["started_on"]["pattern"] == r"^\d{4}-(0[1-9]|1[0-2])$"


@pytest.mark.parametrize(("method", "path"), [("put", "/resumes/me"), ("get", "/resumes/me")])
def test_every_route_requires_authentication(method: str, path: str) -> None:
    response = _client().request(method, path, json={"positions": [], "education": []})

    assert response.status_code == 401
