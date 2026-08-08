"""Die Kopfzeilen einer HTTPException müssen die Antwort erreichen.

Der Anlass war ein Fehler, der sich selbst am Leben hielt: identity-service
löscht bei einem endgültig abgelehnten Refresh das tote Cookie — über
``HTTPException(headers={"Set-Cookie": ...})``, den vorgesehenen Weg. Der
Handler dieser Datei baute daraus eine RFC-9457-Antwort und liess die
Kopfzeilen dabei fallen. Das Cookie blieb also liegen, der Browser versuchte es
beim nächsten Seitenaufruf erneut, wieder 401 — endlos.

Es betrifft nicht nur diesen einen Fall. ``HTTPException(headers=...)`` ist der
normale Weg für ``WWW-Authenticate`` bei 401 (RFC 9110 verlangt es dort sogar)
und für ``Retry-After`` bei 429. Wer das in irgendeinem Dienst benutzt, bekam
bisher wortlos nichts — der Code sah richtig aus und tat nichts.
"""

from __future__ import annotations

from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient
from worker_platform.presentation.errors import register_exception_handlers


def _app() -> FastAPI:
    app = FastAPI()
    register_exception_handlers(app)

    @app.get("/mit-kopf")
    async def mit_kopf() -> dict[str, str]:
        raise HTTPException(
            status_code=401,
            detail="invalid credentials",
            headers={"WWW-Authenticate": 'Bearer realm="workertransfer"'},
        )

    @app.get("/ohne-kopf")
    async def ohne_kopf() -> dict[str, str]:
        raise HTTPException(status_code=404, detail="not found")

    return app


def test_kopfzeilen_der_ausnahme_erreichen_die_antwort() -> None:
    response = TestClient(_app(), raise_server_exceptions=False).get("/mit-kopf")

    assert response.status_code == 401
    assert response.headers["www-authenticate"] == 'Bearer realm="workertransfer"'


def test_der_problem_koerper_bleibt_unveraendert() -> None:
    """Die Kopfzeilen kommen HINZU, sie ersetzen nichts.

    Ohne diese Behauptung liesse sich der Test oben auch dadurch grün machen,
    dass man die ganze Antwort anders baut — und das RFC-9457-Format wäre weg.
    """
    response = TestClient(_app(), raise_server_exceptions=False).get("/mit-kopf")

    assert response.headers["content-type"].startswith("application/problem+json")
    body = response.json()
    assert body["status"] == 401
    assert body["title"] == "Request failed"
    assert body["detail"] == "invalid credentials"
    assert body["type"].endswith("/401")


def test_ohne_kopfzeilen_aendert_sich_nichts() -> None:
    response = TestClient(_app(), raise_server_exceptions=False).get("/ohne-kopf")

    assert response.status_code == 404
    assert response.headers["content-type"].startswith("application/problem+json")
    assert response.json()["detail"] == "not found"
