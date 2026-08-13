"""Die Sammelprüfung im Client — und was sie NICHT tut.

Der Gewinn ist zählbar: eine Runde statt bis zu 40. Genau das prüft der erste
Test, und zwar an der Anzahl der HTTP-Aufrufe — nicht an der Laufzeit, denn eine
Zeitmessung im Test wäre eine Aussage über die Maschine.
"""

from __future__ import annotations

from typing import Any
from uuid import UUID, uuid4

import httpx
import pytest
from profile_service.application.ports import VISIBILITY_CAPABILITY, tenant_capability
from profile_service.infrastructure.consent import ConsentUnavailable, HttpConsentGate
from worker_contracts import MAX_CHECK_BATCH

TENANT = UUID("22222222-2222-2222-2222-222222222222")
BEARER = "token-abc"


def _gate(handler: object) -> HttpConsentGate:
    transport = httpx.MockTransport(handler)  # type: ignore[arg-type]
    return HttpConsentGate(base_url="http://consent:8002", transport=transport)


def _answer(pairs: list[dict[str, Any]], granted: dict[tuple[str, str], bool]) -> httpx.Response:
    return httpx.Response(
        200,
        json={
            "results": [
                {
                    "subject_id": pair["subject_id"],
                    "capability": pair["capability"],
                    "granted": granted.get((pair["subject_id"], pair["capability"]), False),
                    "deleted": False,
                }
                for pair in pairs
            ]
        },
    )


async def test_eine_seite_kostet_eine_runde() -> None:
    """20 Profile, zwei Fähigkeiten je Person — und genau EIN Aufruf.

    Vorher waren das bis zu 40, jeder mit eigenem Verbindungsaufbau.
    """
    subjects = [uuid4() for _ in range(20)]
    aufrufe: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        aufrufe.append(request)
        import json

        pairs = json.loads(request.content)["pairs"]
        return _answer(pairs, {})

    verdicts = await _gate(handler).may_see_many(subjects, tenant_id=TENANT, bearer=BEARER)

    assert len(verdicts) == 20
    assert len(aufrufe) == 1
    assert aufrufe[0].url.path == "/consent/check-batch"


async def test_fragt_beide_faehigkeiten_je_person() -> None:
    """Öffentlich UND für dieses Unternehmen — in einer Anfrage kostet die
    zweite Frage nichts mehr.

    Die Alternative wäre eine zweite Runde für genau die Personen ohne
    öffentliche Freigabe — ein Aufwand, der mit der Anzahl der NICHT
    Freigegebenen steigt und damit selbst eine Auskunft wäre.
    """
    subject = uuid4()
    gesehen: list[tuple[str, str]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        pairs = json.loads(request.content)["pairs"]
        gesehen.extend((p["subject_id"], p["capability"]) for p in pairs)
        return _answer(pairs, {})

    await _gate(handler).may_see_many([subject], tenant_id=TENANT, bearer=BEARER)

    assert gesehen == [
        (str(subject), VISIBILITY_CAPABILITY),
        (str(subject), tenant_capability(TENANT)),
    ]


async def test_oeffentlich_oder_fuer_dieses_unternehmen_genuegt() -> None:
    subjects = [uuid4() for _ in range(3)]

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        pairs = json.loads(request.content)["pairs"]
        return _answer(
            pairs,
            {
                # Erste Person: öffentlich freigegeben.
                (str(subjects[0]), VISIBILITY_CAPABILITY): True,
                # Zweite: nur diesem Unternehmen (etwa durch eine Bewerbung).
                (str(subjects[1]), tenant_capability(TENANT)): True,
                # Dritte: nichts davon.
            },
        )

    verdicts = await _gate(handler).may_see_many(subjects, tenant_id=TENANT, bearer=BEARER)

    assert verdicts == [True, True, False]


async def test_eine_geloeschte_freigabe_oeffnet_nicht() -> None:
    """`DELETE` zieht die Capability logisch zurück — wie in `may_see`."""
    subject = uuid4()

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        pairs = json.loads(request.content)["pairs"]
        return httpx.Response(
            200,
            json={
                "results": [
                    {
                        "subject_id": p["subject_id"],
                        "capability": p["capability"],
                        "granted": True,
                        "deleted": True,
                    }
                    for p in pairs
                ]
            },
        )

    assert await _gate(handler).may_see_many([subject], tenant_id=TENANT, bearer=BEARER) == [False]


async def test_leere_eingabe_fragt_gar_nicht() -> None:
    """Eine Anfrage, deren Ergebnis feststeht, ist nur Rauschen im Ledger-Log."""
    aufrufe: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        aufrufe.append(request)
        return httpx.Response(200, json={"results": []})

    assert await _gate(handler).may_see_many([], tenant_id=TENANT, bearer=BEARER) == []
    assert aufrufe == []


async def test_ein_stummer_ledger_wirft_statt_zu_verneinen() -> None:
    """Dieselbe Regel wie bei `may_see`: `False` wäre eine Aussage über eine
    Person, die niemand treffen kann. Der Router bildet das auf 503 ab."""

    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("weg")

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see_many([uuid4()], tenant_id=TENANT, bearer=BEARER)


async def test_eine_antwort_die_nicht_zu_den_fragen_passt_wird_nicht_geraten() -> None:
    """Falsch zuzuordnen hieße, das Profil der falschen Person zu zeigen."""

    def handler(request: httpx.Request) -> httpx.Response:
        # Zwei Fragen (eine Person, zwei Fähigkeiten), aber nur eine Antwort.
        return httpx.Response(
            200,
            json={
                "results": [
                    {
                        "subject_id": str(uuid4()),
                        "capability": "x",
                        "granted": True,
                        "deleted": False,
                    }
                ]
            },
        )

    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see_many([uuid4()], tenant_id=TENANT, bearer=BEARER)


async def test_mehr_als_der_vertrag_traegt_wird_laut_abgelehnt() -> None:
    """Und nicht still als „nicht freigegeben" — das wäre ein versteckter Fehler.

    Zwei Fähigkeiten je Person, also ist die Grenze bei `MAX_CHECK_BATCH / 2`
    Personen erreicht.
    """

    def handler(request: httpx.Request) -> httpx.Response:  # pragma: no cover
        raise AssertionError("hätte gar nicht fragen dürfen")

    zu_viele = [uuid4() for _ in range(MAX_CHECK_BATCH // 2 + 1)]
    with pytest.raises(ConsentUnavailable):
        await _gate(handler).may_see_many(zu_viele, tenant_id=TENANT, bearer=BEARER)


async def test_die_grenze_deckt_die_groesste_seite_ab() -> None:
    """Der Vertrag muss tragen, was profile-service höchstens anfragt.

    Sonst wäre die Sammelprüfung genau dann unbrauchbar, wenn sie am meisten
    hilft — bei der größten Seite.
    """
    from profile_service.application.handlers import MAX_PAGE_SIZE

    assert MAX_PAGE_SIZE * 2 <= MAX_CHECK_BATCH
