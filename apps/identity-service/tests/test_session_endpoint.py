"""`GET /auth/session` — die öffentliche Frage „ist gerade jemand angemeldet?".

Warum es diesen Endpunkt neben `/me` gibt, und warum `/me` nicht geändert wurde:

`/me` heisst „gib mir mein Profil". Das ist eine geschützte Ressource, und ohne
Nachweis ist **401 die richtige Antwort** (RFC 9110). Die Oberfläche stellt aber
bei jedem Seitenaufruf eine ganz andere Frage — „ist gerade jemand angemeldet?"
—, und die ist öffentlich. Sie über `/me` zu stellen erzeugte für jeden
abgemeldeten Besucher einen 401 im Netzwerkprotokoll: inhaltlich korrekt, aber
die falsche Frage am falschen Endpunkt.

Der dritte Zustand ist der eigentliche Gewinn. `renewable` heisst: das
Access-Token trägt nicht mehr, ein Refresh-Cookie liegt aber vor. Erst damit
kann die Oberfläche `POST /auth/refresh` **gezielt** aufrufen statt auf gut Glück
— und ein anonymer Besucher stellt gar keine zweite Anfrage.
"""

from __future__ import annotations

from fastapi.testclient import TestClient
from identity_service.configuration import IdentityServiceSettings
from identity_service.main import create_app


def _client() -> TestClient:
    return TestClient(create_app(IdentityServiceSettings()))


def test_anonym_antwortet_200_und_nicht_401() -> None:
    """Der ganze Zweck: kein Fehler für jemanden, der nur liest.

    Ein 401 ist hier nicht bloss unschön — er sagt „etwas ist schiefgegangen"
    über einen Zustand, der völlig normal ist.
    """
    response = _client().get("/auth/session")

    assert response.status_code == 200
    assert response.json() == {"user": None, "state": "anonymous"}


def test_ein_totes_access_cookie_allein_macht_die_sitzung_nicht_erneuerbar() -> None:
    """Ohne Refresh-Cookie gibt es nichts zu erneuern.

    Würde hier `renewable` stehen, riefe die Oberfläche `POST /auth/refresh`
    auf, bekäme 401 — und wir hätten den Fehler, den dieser Endpunkt abschafft,
    nur eine Anfrage später wieder.
    """
    client = _client()

    response = client.get("/auth/session", cookies={"access": "abgelaufen.kaputt.wert"})

    assert response.status_code == 200
    assert response.json() == {"user": None, "state": "anonymous"}


def test_mit_refresh_cookie_heisst_es_erneuerbar() -> None:
    """Genau dann — und nur dann — lohnt sich POST /auth/refresh.

    Der Inhalt wird hier NICHT geprüft: das täte der Refresh selbst, und ein
    zweiter Ort, der Token bewertet, wäre ein zweiter Ort, an dem er falsch
    liegen kann. Dieser Endpunkt sagt nur, ob ein Versuch überhaupt Sinn ergibt.
    """
    client = _client()

    response = client.get("/auth/session", cookies={"refresh": "irgendein.refresh.wert"})

    assert response.status_code == 200
    assert response.json() == {"user": None, "state": "renewable"}


def test_der_endpunkt_verrät_nichts_ueber_die_person() -> None:
    """Öffentlich heisst: er darf keine Angriffsfläche für Aufzählung sein.

    Ohne gültige Anmeldung ist die Antwort byte-identisch, egal was an Cookies
    mitkommt — es gibt also nichts, woraus jemand auf einen Kontostand,
    eine Adresse oder auch nur deren Existenz schliessen könnte.
    """
    client = _client()

    ohne = client.get("/auth/session").json()
    mit_muell = client.get("/auth/session", cookies={"access": "aaa.bbb.ccc"}).json()

    assert ohne == mit_muell


def test_die_route_steht_im_schema_und_me_bleibt_wie_es_war() -> None:
    schema = create_app(IdentityServiceSettings()).openapi()["paths"]

    assert "/auth/session" in schema
    # /me bleibt geschützt und antwortet weiterhin 401 ohne Nachweis. Dieser
    # Endpunkt ersetzt es nicht, er beantwortet eine andere Frage.
    assert "/me" in schema


def test_die_sitzungsabfrage_wird_nicht_gebremst() -> None:
    """Sie läuft bei JEDEM Seitenaufruf — eine Bremse hier bremst das Lesen.

    Die Auth-Bremse (ROADMAP 10.1) sitzt bewusst an den teuren Endpunkten:
    Anmelden rechnet bcrypt, Registrieren verschickt Mail. Diese Abfrage tut
    für einen Anonymen gar nichts — kein Datenbankzugriff, kein Hashen. Wer
    „alles unter /auth" bremst, macht daraus eine Grenze fürs blosse Blättern,
    und die fällt erst jemandem auf, der viel liest.
    """
    from identity_service.presentation.compose_api import AUTH_LIMITS

    assert ("GET", "/auth/session") not in AUTH_LIMITS
    # Die teuren bleiben gebremst — sonst hätte dieser Test sie mit entschärft.
    assert ("POST", "/auth/login") in AUTH_LIMITS
    assert ("POST", "/auth/register") in AUTH_LIMITS
