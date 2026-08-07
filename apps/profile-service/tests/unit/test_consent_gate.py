"""Der Consent-Client — inklusive dessen, was bei Störungen passiert."""

from __future__ import annotations

from uuid import UUID, uuid4

import httpx
import pytest
from profile_service.infrastructure.consent import ConsentUnavailable, HttpConsentGate

SUBJECT = uuid4()
TENANT = UUID("22222222-2222-2222-2222-222222222222")
BEARER = "token-abc"


def _gate(handler: object) -> HttpConsentGate:
    transport = httpx.MockTransport(handler)  # type: ignore[arg-type]
    return HttpConsentGate(base_url="http://consent:8002", transport=transport)


async def test_a_granted_capability_opens_the_gate() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={"subject_id": str(SUBJECT), "capability": "x", "granted": True, "deleted": False},
        )

    assert await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER) is True


async def test_a_withheld_capability_closes_it() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={
                "subject_id": str(SUBJECT),
                "capability": "x",
                "granted": False,
                "deleted": False,
            },
        )

    assert await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER) is False


async def test_a_deleted_consent_closes_it_too() -> None:
    # DELETE zieht die Capability logisch zurück; granted ist dann ohnehin
    # false, aber die Absicht soll auch dann stimmen, wenn der Ledger beides
    # meldet.
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={"subject_id": str(SUBJECT), "capability": "x", "granted": True, "deleted": True},
        )

    assert await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER) is False


async def test_the_callers_token_is_forwarded() -> None:
    seen: dict[str, str] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["auth"] = request.headers.get("authorization", "")
        return httpx.Response(
            200,
            json={"subject_id": str(SUBJECT), "capability": "x", "granted": True, "deleted": False},
        )

    await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)

    # Der Service handelt im Auftrag des Aufrufers, nicht mit eigenem Konto —
    # ein Service-Account wäre ein zweiter Vertrauensweg ohne Gewinn.
    assert seen["auth"] == f"Bearer {BEARER}"


async def test_the_asked_capability_is_the_visibility_one() -> None:
    seen: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        seen.update(json.loads(request.content))
        return httpx.Response(
            200,
            json={"subject_id": str(SUBJECT), "capability": "x", "granted": True, "deleted": False},
        )

    await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)

    assert seen["capability"] == "profile.visibility:public"
    assert seen["subject_id"] == str(SUBJECT)


async def test_an_unreachable_ledger_fails_closed() -> None:
    """Fail closed, und zwar hörbar.

    Weder das Profil zeigen (wir wissen nicht, ob wir dürfen) noch 'nicht
    freigegeben' behaupten (das wäre eine Aussage über die Person, die wir nicht
    treffen können).
    """

    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("consent-service down")

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)


async def test_a_server_error_from_the_ledger_fails_closed() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(500, text="boom")

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)


async def test_an_unauthorised_call_fails_closed_rather_than_denying() -> None:
    # 401 heißt: unser Token taugt nicht. Das ist ein Systemproblem, keine
    # Aussage darüber, ob die Person eingewilligt hat.
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(401, json={"detail": "not authenticated"})

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)


async def test_a_nonsense_body_fails_closed() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, text="kein json")

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see(SUBJECT, tenant_id=TENANT, bearer=BEARER)
