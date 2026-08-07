"""V1 der ADR-0027: ein Zusteller, der **scheitern kann**.

Das ist keine Kleinigkeit, sondern die Voraussetzung dafür, dass der ganze
Nachweis aus §4 etwas wert ist.

**Der Befund, der dazu führte.** Der produktive `HttpNotifier` fängt *jede*
Ausnahme und prüft die Antwort nicht — wortgleich in transfer-, applications-
und resume-service. `OutboxDispatcher._deliver` setzt `delivered_at`, sobald
`notify` ohne Ausnahme zurückkehrt. Ein `ConnectError` oder ein `500` wird damit
als **zugestellt** verbucht. Für eine Mail ist das Schlucken richtig (ADR-0025);
für eine Löschung wäre `delivered_at` exakt die Lüge, die diese ADR verhindern
soll.

Bewiesen war der Wiederholungspfad bislang nur gegen einen `RecordingNotifier`,
und der **wirft** — eine Attrappe, die sich anders verhält als der Code, den sie
vertritt. Deshalb laufen diese Tests gegen den echten Adapter, nur mit einem
Transport, der in den Prozess statt ins Netz zeigt.
"""

from __future__ import annotations

from uuid import uuid4

import httpx
import pytest
from identity_service.infrastructure.erasure import (
    ERASURE_SECRET_HEADER,
    ErasureUndelivered,
    HttpErasureDelivery,
)

TARGETS = {"profile": "http://profile-service:8000", "jobs": "http://jobs-service:8000"}
SECRET = "erasure-secret-with-at-least-thirty-two-bytes"


def _delivery(handler: object, *, secret: str = SECRET) -> HttpErasureDelivery:
    return HttpErasureDelivery(
        targets=TARGETS,
        secret=secret,
        transport=httpx.MockTransport(handler),  # type: ignore[arg-type]
    )


async def test_a_transport_error_raises_instead_of_being_swallowed() -> None:
    def explode(_request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("Dienst nicht erreichbar")

    with pytest.raises(ErasureUndelivered):
        await _delivery(explode).send("profile", uuid4())


@pytest.mark.parametrize("code", [400, 401, 404, 409, 500, 503])
async def test_any_non_2xx_answer_is_a_failure(code: int) -> None:
    """Genau der Fall, den `HttpNotifier` nicht einmal ansieht.

    Ein `404` heißt hier: der Empfänger kennt uns nicht oder das Geheimnis
    stimmt nicht. Beides ist ein Grund, es weiter zu versuchen — und niemals
    ein Grund, `delivered_at` zu setzen.
    """
    with pytest.raises(ErasureUndelivered):
        await _delivery(lambda _r: httpx.Response(code)).send("profile", uuid4())


async def test_a_2xx_answer_reports_what_the_recipient_retained() -> None:
    retained = await _delivery(lambda _r: httpx.Response(200, json={"retained": 0})).send(
        "profile", uuid4()
    )

    assert retained == 0


async def test_it_presents_the_secret_and_the_subject() -> None:
    seen: dict[str, object] = {}
    subject = uuid4()

    def capture(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["secret"] = request.headers.get(ERASURE_SECRET_HEADER)
        seen["body"] = request.content.decode()
        return httpx.Response(200, json={"retained": 0})

    await _delivery(capture).send("profile", subject)

    assert seen["url"] == "http://profile-service:8000/internal/erasure"
    assert seen["secret"] == SECRET
    assert str(subject) in str(seen["body"])


async def test_an_empty_secret_is_a_failure_not_a_quiet_skip() -> None:
    """`HttpNotifier` kehrt bei leerem Geheimnis einfach zurück — für eine Mail
    richtig, hier fatal: die Zeile gälte als zugestellt, und niemand hätte
    gelöscht."""
    with pytest.raises(ErasureUndelivered):
        await _delivery(lambda _r: httpx.Response(200), secret="").send("profile", uuid4())


async def test_an_unknown_recipient_is_a_failure_not_a_success() -> None:
    """Sonst würde ein Tippfehler in der Empfängerliste zu einer Löschung, die
    sich selbst für erledigt erklärt."""
    with pytest.raises(ErasureUndelivered):
        await _delivery(lambda _r: httpx.Response(200)).send("gibtsnicht", uuid4())


async def test_the_company_withdrawal_names_the_company_not_a_person() -> None:
    seen: dict[str, str] = {}
    tenant = uuid4()

    def capture(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["body"] = request.content.decode()
        return httpx.Response(200)

    await _delivery(capture).withdraw_company(tenant)

    assert seen["url"] == "http://jobs-service:8000/internal/company-withdrawal"
    assert "tenant_id" in seen["body"]
    assert "user_id" not in seen["body"]
