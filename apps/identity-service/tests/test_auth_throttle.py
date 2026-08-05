"""Die Bremse am echten Auth-Rand — verdrahtet, nicht nur vorhanden.

`packages/worker-platform/tests/test_throttle.py` prüft das Verhalten des
Bremsklotzes. Hier geht es um die andere Hälfte: dass er in `identity-service`
tatsächlich hängt, an den richtigen Pfaden, und dass er nichts bremst, was
niemand raten kann.
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient
from identity_service.configuration import IdentityServiceSettings
from identity_service.main import create_app
from identity_service.presentation.compose_api import AUTH_LIMITS, throttle_limits
from worker_platform.configuration import Environment

#: Ein einsetzbares Geheimnis. `IdentityServiceSettings` verweigert den
#: Entwicklungswert außerhalb der niedrigen Umgebungen — zu Recht, und deshalb
#: braucht jeder Test mit STAGING/PRODUCTION hier einen echten.
DEPLOYABLE_SECRET = "ein-geheimnis-mit-mehr-als-zweiunddreissig-zeichen"


def _settings(**overrides: object) -> IdentityServiceSettings:
    return IdentityServiceSettings(jwt_secret=DEPLOYABLE_SECRET, **overrides)  # type: ignore[arg-type]


class TestWhenItIsOn:
    @pytest.mark.parametrize(
        ("environment", "expected"),
        [
            (Environment.LOCAL, False),
            (Environment.TEST, False),
            (Environment.DEVELOPMENT, True),
            (Environment.STAGING, True),
            (Environment.PRODUCTION, True),
        ],
    )
    def test_it_is_off_only_where_every_request_shares_one_address(
        self, environment: Environment, expected: bool
    ) -> None:
        """Im Compose-Stack kommt jede Anfrage von derselben Gateway-Adresse.

        Eine Bremse je Herkunft träfe dort die eigene Testreihe statt eines
        Angreifers — und eine Maßnahme, die den normalen Betrieb trifft, wird
        abgeschaltet und ist danach gar keine.
        """
        limits = throttle_limits(_settings(environment=environment))

        assert (limits is not None) is expected

    def test_it_can_be_switched_on_locally_to_look_at_it(self) -> None:
        # Eine Sicherheitsmaßnahme, die man lokal nicht sehen kann, glaubt man
        # nur.
        limits = throttle_limits(
            _settings(environment=Environment.LOCAL, auth_throttle_enabled=True)
        )

        assert limits is not None

    def test_it_can_be_switched_off_in_production_deliberately(self) -> None:
        # Hinter einem Gateway, das selbst bremst, wäre die zweite Bremse nur
        # eine zweite Wahrheit über dieselbe Grenze.
        limits = throttle_limits(
            _settings(environment=Environment.PRODUCTION, auth_throttle_enabled=False)
        )

        assert limits is None


class TestWhatItCovers:
    def test_every_guessable_auth_endpoint_is_named(self) -> None:
        """Der Test, der die Lücke findet, wenn jemand einen Endpunkt ergänzt.

        Gebremst gehört, wo geraten oder fremder Posteingang gefüllt werden
        kann. `/auth/logout` gehört ausdrücklich nicht dazu: es braucht ein
        gültiges Token, und wer eines hat, muss nichts raten.
        """
        assert set(AUTH_LIMITS) == {
            ("POST", "/auth/login"),
            ("POST", "/auth/verify-email"),
            ("POST", "/auth/refresh"),
            ("POST", "/auth/register"),
            ("POST", "/auth/resend-verification"),
        }

    def test_the_strictest_limit_is_the_one_that_mails_a_stranger(self) -> None:
        # `resend-verification` schickt eine Mail an eine Adresse, die der
        # Aufrufer NENNT. Ohne enge Grenze ist das ein Weg, einen fremden
        # Posteingang zu fluten — die Grenze schützt hier nicht uns.
        per_hour = {key: limit.times * 3600 / limit.seconds for key, limit in AUTH_LIMITS.items()}
        strictest = min(per_hour, key=lambda key: per_hour[key])

        assert strictest == ("POST", "/auth/resend-verification")


class TestWiredUp:
    def _client(self) -> TestClient:
        # `raise_server_exceptions=False`, weil hier ABSICHTLICH keine Datenbank
        # läuft: die ersten zehn Versuche erreichen den Handler und scheitern
        # dort mit 500. Genau das macht den Test aussagekräftig — siehe unten.
        return TestClient(
            create_app(_settings(environment=Environment.LOCAL, auth_throttle_enabled=True)),
            raise_server_exceptions=False,
        )

    def test_the_eleventh_attempt_never_reaches_the_handler(self) -> None:
        """500, 500, … und dann 429 — und der Unterschied ist der Beweis.

        Ohne Postgres scheitert jeder Versuch, der beim Handler ankommt, mit
        500. Der elfte antwortet 429, also ist er GAR NICHT angekommen: die
        Bremse steht weiter außen als die Authentifizierung.

        Das ist keine Feinheit. Läge sie innen, würde für jeden Rateversuch
        erst bcrypt gerechnet — die Bremse wäre der teuerste Teil des Angriffs
        und damit selbst die Waffe.
        """
        client = self._client()
        body = {"email": "wer@example.com", "password": "falsch"}

        codes = [client.post("/auth/login", json=body).status_code for _ in range(11)]

        assert codes[-1] == 429
        assert codes.count(429) == 1
        # Die ersten zehn sind durchgelassen worden — die Bremse zählt, sie
        # sperrt nicht vorsorglich.
        assert 429 not in codes[:10]

    def test_the_refusal_carries_a_retry_after_and_no_account_detail(self) -> None:
        client = self._client()
        body = {"email": "verraet-mich-nicht@example.com", "password": "falsch"}
        for _ in range(10):
            client.post("/auth/login", json=body)

        refused = client.post("/auth/login", json=body)

        assert refused.status_code == 429
        assert int(refused.headers["Retry-After"]) > 0
        assert "verraet-mich-nicht" not in refused.text

    def test_health_is_never_braked(self) -> None:
        # Sonst würde eine Bremse den Bereitschaftsprüfer aussperren und der
        # Orchestrator den Dienst neu starten — die Bremse als Ausfallgrund.
        client = self._client()

        assert all(client.get("/health/live").status_code == 200 for _ in range(50))
