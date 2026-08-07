"""App-level smoke: the platform wiring is actually in place.

No database and no Docker — this only proves the composition root produced an
app with correlation IDs, security headers and closed docs.
"""

from uuid import UUID

from fastapi.testclient import TestClient
from github_service.configuration import GithubServiceSettings
from github_service.main import create_app


def test_liveness_is_available_with_correlation_and_security_headers() -> None:
    client = TestClient(create_app(GithubServiceSettings()))

    response = client.get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "github-service"}
    assert str(UUID(response.headers["x-correlation-id"])) == response.headers["x-correlation-id"]
    assert response.headers["x-content-type-options"] == "nosniff"
    assert response.headers["x-frame-options"] == "DENY"


def test_valid_correlation_id_is_propagated() -> None:
    client = TestClient(create_app(GithubServiceSettings()))
    correlation_id = "1f46520e-796a-4abf-9502-835a42046737"

    response = client.get("/health/ready", headers={"X-Correlation-ID": correlation_id})

    assert response.status_code == 200
    assert response.headers["x-correlation-id"] == correlation_id


def test_docs_are_closed_by_default() -> None:
    client = TestClient(create_app(GithubServiceSettings()))

    assert client.get("/docs").status_code == 404
