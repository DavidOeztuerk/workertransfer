"""App-level wiring — the proof that the placeholder compose_api is gone.

Until Phase 3 this service built a bare FastAPI() and therefore served no
correlation ID, no security headers and no RFC-9457 problem details. These tests
fail if anyone reintroduces that shortcut. No database and no Docker needed:
create_async_engine is lazy and /health/live never opens a connection.
"""

from uuid import UUID

from consent_service.configuration import ConsentServiceSettings
from consent_service.main import create_app
from fastapi.testclient import TestClient


def _client() -> TestClient:
    return TestClient(create_app(ConsentServiceSettings()))


def test_liveness_reports_this_service() -> None:
    response = _client().get("/health/live")

    assert response.status_code == 200
    assert response.json() == {"status": "ok", "service": "consent-service"}


def test_security_headers_are_present() -> None:
    response = _client().get("/health/live")

    assert response.headers["x-content-type-options"] == "nosniff"
    assert response.headers["x-frame-options"] == "DENY"
    assert "default-src 'none'" in response.headers["content-security-policy"]


def test_correlation_id_is_generated_when_absent() -> None:
    response = _client().get("/health/live")

    correlation_id = response.headers["x-correlation-id"]
    assert str(UUID(correlation_id)) == correlation_id


def test_correlation_id_is_propagated_when_supplied() -> None:
    correlation_id = "1f46520e-796a-4abf-9502-835a42046737"

    response = _client().get("/health/live", headers={"X-Correlation-ID": correlation_id})

    assert response.headers["x-correlation-id"] == correlation_id


def test_docs_are_closed_by_default() -> None:
    assert _client().get("/docs").status_code == 404


def test_consent_endpoints_require_authentication() -> None:
    # No token: the auth middleware leaves state.user as None and the router
    # refuses. A ledger that answered anonymously would leak who consented to what.
    client = _client()
    body = {
        "subject_id": "11111111-1111-1111-1111-111111111111",
        "capability": "profile.visibility:public",
    }

    for path in ("/consent/grant", "/consent/check"):
        assert client.post(path, json=body).status_code == 401, path


def test_unauthenticated_error_is_a_problem_document() -> None:
    # The kernel's exception handlers turn HTTPException into RFC 9457, which the
    # placeholder app never did.
    response = _client().post(
        "/consent/check",
        json={
            "subject_id": "11111111-1111-1111-1111-111111111111",
            "capability": "profile.visibility:public",
        },
    )

    assert response.headers["content-type"].startswith("application/problem+json")
    body = response.json()
    assert body["status"] == 401
    assert "correlationId" in body


def test_malformed_body_is_rejected_before_any_database_work() -> None:
    response = _client().post("/consent/check", json={"capability": "x.y"})

    assert response.status_code == 422
    assert response.headers["content-type"].startswith("application/problem+json")


def _schema() -> dict:
    # Straight off the app, not over HTTP: /openapi.json is only served when
    # enable_docs is on, and this contract must hold in every environment.
    return create_app(ConsentServiceSettings()).openapi()


def _response_properties(schema: dict, path: str) -> dict:
    ref = schema["paths"][path]["post"]["responses"]["200"]["content"]["application/json"][
        "schema"
    ]["$ref"]
    return schema["components"]["schemas"][ref.rsplit("/", 1)[-1]]["properties"]


def test_check_never_exposes_the_withdrawal_reason() -> None:
    # /consent/check answers about ANY subject to ANY authenticated caller, so the
    # free-text reason the subject wrote must not ride along. Asserted against the
    # schema consumers generate clients from, and it needs no database.
    assert "reason" not in _response_properties(_schema(), "/consent/check")


def test_the_subject_still_gets_its_own_reason_back() -> None:
    # The write endpoints refuse an actor that is not the subject, so returning the
    # reason there discloses nothing the caller did not just write.
    schema = _schema()

    # `/consent/delete` steht hier nicht mehr: der kapabilitätsbezogene
    # Endpunkt ist zurückgezogen (ADR-0027 §1). Die Kontolöschung hat kein
    # Begründungsfeld — weder in der Anfrage noch in der Antwort.
    for path in ("/consent/grant", "/consent/revoke"):
        assert "reason" in _response_properties(schema, path), path
    assert "/consent/delete" not in schema["paths"]
