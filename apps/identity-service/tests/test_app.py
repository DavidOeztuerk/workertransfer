from uuid import UUID

from fastapi.testclient import TestClient
from identity_service.configuration import IdentityServiceSettings
from identity_service.main import create_app


def test_liveness_is_available_with_correlation_and_security_headers() -> None:
    client = TestClient(create_app(IdentityServiceSettings()))

    response = client.get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "identity-service"}
    assert str(UUID(response.headers["x-correlation-id"])) == response.headers["x-correlation-id"]
    assert response.headers["x-content-type-options"] == "nosniff"
    assert response.headers["x-frame-options"] == "DENY"


def test_valid_correlation_id_is_propagated() -> None:
    client = TestClient(create_app(IdentityServiceSettings()))
    correlation_id = "1f46520e-796a-4abf-9502-835a42046737"

    response = client.get("/health/ready", headers={"X-Correlation-ID": correlation_id})

    assert response.status_code == 200
    assert response.headers["x-correlation-id"] == correlation_id


def test_docs_are_closed_by_default() -> None:
    client = TestClient(create_app(IdentityServiceSettings()))

    assert client.get("/docs").status_code == 404
