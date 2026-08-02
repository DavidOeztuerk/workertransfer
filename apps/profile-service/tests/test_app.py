"""App-Verdrahtung: Routen, Verträge und die Zugangsregeln.

Kein Docker nötig — create_async_engine ist träge und /health/live fasst die
Datenbank nie an.
"""

from __future__ import annotations

from uuid import UUID

import pytest
from fastapi.testclient import TestClient
from profile_service.configuration import ProfileServiceSettings
from profile_service.main import create_app


def _client() -> TestClient:
    return TestClient(create_app(ProfileServiceSettings()))


def _schema() -> dict:
    # Direkt von der App: /openapi.json wird nur bei enable_docs ausgeliefert,
    # der Vertrag muss aber überall gelten.
    return create_app(ProfileServiceSettings()).openapi()


def test_liveness_reports_this_service() -> None:
    response = _client().get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "profile-service"}
    assert str(UUID(response.headers["x-correlation-id"])) == response.headers["x-correlation-id"]
    assert response.headers["x-content-type-options"] == "nosniff"


def test_all_four_profile_routes_exist() -> None:
    paths = _schema()["paths"]

    assert "/profiles/me" in paths
    assert "/profiles/{subject_id}" in paths
    assert "/profiles" in paths


def test_the_save_body_carries_no_visibility_flag() -> None:
    """Sichtbarkeit gehört dem Consent-Ledger, nicht dem Profil.

    Ein Feld hier wäre eine zweite Wahrheit — und eine, die der Client setzen
    könnte.
    """
    schema = _schema()
    ref = schema["paths"]["/profiles/me"]["put"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"headline", "bio", "location", "remote_ok", "skills"}
    assert "visible" not in props
    assert "consent" not in props


@pytest.mark.parametrize(
    ("method", "path"),
    [
        ("put", "/profiles/me"),
        ("get", "/profiles/me"),
        ("get", "/profiles/11111111-1111-1111-1111-111111111111"),
        ("get", "/profiles"),
    ],
)
def test_every_route_requires_authentication(method: str, path: str) -> None:
    client = _client()

    response = client.request(method, path, json={"headline": "x"})

    assert response.status_code == 401
