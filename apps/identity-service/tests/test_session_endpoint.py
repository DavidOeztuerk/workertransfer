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


# Cookies werden am CLIENT gesetzt, nicht je Anfrage: starlette verwirft
# `cookies=` pro Request (DeprecationWarning), weil dabei unklar ist, ob das
# Cookie danach im Krug bleibt. Am Client ist die Antwort eindeutig — und jeder
# Test baut sich hier ohnehin seinen eigenen.
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

    client.cookies.set("access", "abgelaufen.kaputt.wert")
    response = client.get("/auth/session")

    assert response.status_code == 200
    assert response.json() == {"user": None, "state": "anonymous"}


def test_mit_refresh_cookie_heisst_es_erneuerbar() -> None:
    """Genau dann — und nur dann — lohnt sich POST /auth/refresh.

    Der Inhalt wird hier NICHT geprüft: das täte der Refresh selbst, und ein
    zweiter Ort, der Token bewertet, wäre ein zweiter Ort, an dem er falsch
    liegen kann. Dieser Endpunkt sagt nur, ob ein Versuch überhaupt Sinn ergibt.
    """
    client = _client()

    client.cookies.set("refresh", "irgendein.refresh.wert")
    response = client.get("/auth/session")

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
    # Erst NACH der ersten Anfrage setzen — die Reihenfolge ist hier die Aussage:
    # ohne Cookie, dann mit Müll, und beide Antworten müssen gleich sein.
    client.cookies.set("access", "aaa.bbb.ccc")
    mit_muell = client.get("/auth/session").json()

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


def test_ein_abgelehnter_refresh_raeumt_sein_totes_cookie_weg() -> None:
    """Sonst versucht der Browser es bei JEDEM Seitenaufruf erneut — ewig.

    Die Kette ohne diese Zeile: `/auth/session` sieht ein Refresh-Cookie und
    meldet `renewable`; die Oberfläche ruft daraufhin `POST /auth/refresh`; das
    Token trägt nicht mehr, also 401. Beim nächsten Seitenaufruf liegt dasselbe
    tote Cookie noch da, und alles wiederholt sich. Ein Fehler, der sich selbst
    am Leben hält.

    Absichtlich nur das REFRESH-Cookie: es ist dasjenige, das `renewable`
    auslöst. Ein totes Access-Cookie allein führt bereits zu `anonymous` und
    stört niemanden — es läuft nach 15 Minuten von selbst ab, und es zusätzlich
    zu löschen bräuchte einen zweiten Set-Cookie-Kopf an einer Ausnahme, die
    nur einen tragen kann.
    """
    client = _client()

    client.cookies.set("refresh", "totes.token.hier")
    antwort = client.post("/auth/refresh")

    assert antwort.status_code == 401
    gesetzt = antwort.headers.get("set-cookie", "")
    assert "refresh=" in gesetzt, f"kein Set-Cookie zum Löschen: {gesetzt!r}"
    # Max-Age=0 bzw. ein Ablauf in der Vergangenheit — beides löscht.
    assert "Max-Age=0" in gesetzt or "01 Jan 1970" in gesetzt
    # Der Pfad MUSS zum Setzen passen, sonst löscht der Browser nichts.
    assert "Path=/auth" in gesetzt


def test_nach_dem_aufraeumen_ist_der_zustand_wieder_anonymous() -> None:
    """Der Beleg, dass die Schleife wirklich endet.

    Ohne Refresh-Cookie meldet die Sitzungsabfrage `anonymous`, und die
    Oberfläche stellt gar keine zweite Anfrage mehr.
    """
    client = _client()

    client.cookies.set("access", "totes.access.token")
    antwort = client.get("/auth/session")

    assert antwort.json() == {"user": None, "state": "anonymous"}
