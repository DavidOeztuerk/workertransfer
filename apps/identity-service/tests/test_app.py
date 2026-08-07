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


def _schema() -> dict:
    # Direkt von der App, nicht über HTTP: /openapi.json wird nur bei
    # enable_docs ausgeliefert, dieser Vertrag muss aber überall gelten.
    return create_app(IdentityServiceSettings()).openapi()


def test_the_new_auth_and_company_routes_exist() -> None:
    paths = _schema()["paths"]

    assert "/auth/verify-email" in paths
    assert "/auth/resend-verification" in paths
    assert "/companies" in paths
    assert "/me/companies" in paths
    assert "/auth/company/{tenant_id}" in paths
    # Umbenannt: das Infrastrukturwort verlässt die öffentliche Grenze.
    assert "/auth/tenant/{tenant_id}" not in paths


def test_create_company_body_cannot_carry_a_domain() -> None:
    # Die Domain wird serverseitig aus der bestätigten Adresse abgeleitet. Wäre
    # sie sendbar, könnte sich jemand eine fremde Firma zuschreiben.
    schema = _schema()
    ref = schema["paths"]["/companies"]["post"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"name"}


def test_register_body_cannot_carry_a_tenant() -> None:
    schema = _schema()
    ref = schema["paths"]["/auth/register"]["post"]["requestBody"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    props = schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]

    assert set(props) == {"email", "password", "display_name"}


def test_creating_a_company_without_a_token_is_rejected() -> None:
    client = TestClient(create_app(IdentityServiceSettings()))

    response = client.post("/companies", json={"name": "Firma"})

    assert response.status_code == 401


def test_listing_companies_without_a_token_is_rejected() -> None:
    client = TestClient(create_app(IdentityServiceSettings()))

    response = client.get("/me/companies")

    assert response.status_code == 401
