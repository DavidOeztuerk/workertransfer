"""Der Bremsklotz am Auth-Rand.

Er beantwortet den einzigen `TODO`-Marker, der im ganzen Code stand: der
Anmeldeweg hatte keine Bremse gegen Durchprobieren.
"""

from __future__ import annotations

from typing import Any

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from worker_platform.presentation.throttle import (
    Limit,
    SlidingWindowLimiter,
    ThrottleMiddleware,
    client_ip,
)


class FakeClock:
    def __init__(self) -> None:
        self.now = 1000.0

    def __call__(self) -> float:
        return self.now


class TestSlidingWindow:
    def test_it_allows_up_to_the_limit_and_then_stops(self) -> None:
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=3, seconds=60)

        assert [limiter.hit("a", limit).allowed for _ in range(4)] == [True, True, True, False]

    def test_the_window_slides_instead_of_resetting_on_the_hour(self) -> None:
        """Ein Fenster, das zur vollen Minute aufmacht, lädt zum Warten ein.

        Wer die Grenze kennt, probiert dann im Takt weiter. Ein gleitendes
        Fenster gibt jeden Versuch einzeln wieder frei.
        """
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=2, seconds=60)

        limiter.hit("a", limit)
        clock.now += 30
        limiter.hit("a", limit)
        assert limiter.hit("a", limit).allowed is False

        # 31 Sekunden später ist der ERSTE Versuch aus dem Fenster gefallen —
        # nicht beide.
        clock.now += 31
        assert limiter.hit("a", limit).allowed is True
        assert limiter.hit("a", limit).allowed is False

    def test_a_denied_attempt_does_not_extend_the_lockout(self) -> None:
        """Sonst hält sich eine Sperre selbst am Leben.

        Genau diesen Fehler hat `worker-ratelimit` (unbenutzt, Redis): es zählt
        den abgelehnten Versuch mit. Wer dagegen läuft, kommt nie wieder rein,
        solange er es weiter versucht.
        """
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=1, seconds=60)

        limiter.hit("a", limit)
        for _ in range(50):
            clock.now += 1
            assert limiter.hit("a", limit).allowed is False

        clock.now += 11  # 61 s nach dem einen gezählten Versuch
        assert limiter.hit("a", limit).allowed is True

    def test_keys_do_not_share_a_budget(self) -> None:
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=1, seconds=60)

        assert limiter.hit("a", limit).allowed is True
        assert limiter.hit("b", limit).allowed is True

    def test_retry_after_names_when_the_oldest_attempt_falls_out(self) -> None:
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=1, seconds=60)

        limiter.hit("a", limit)
        clock.now += 20

        assert limiter.hit("a", limit).retry_after == 40

    def test_it_forgets_keys_whose_window_has_passed(self) -> None:
        """Sonst wäre die Bremse selbst ein Speicherleck.

        Ein Angreifer mit vielen Adressen würde eine Tabelle füllen, die nie
        wieder kleiner wird.
        """
        clock = FakeClock()
        limiter = SlidingWindowLimiter(clock=clock)
        limit = Limit(times=1, seconds=60)

        for index in range(100):
            limiter.hit(f"key-{index}", limit)
        clock.now += 61
        limiter.hit("noch-einer", limit)

        assert limiter.tracked_keys == 1


class TestClientIp:
    def test_it_uses_the_socket_peer_and_ignores_the_header(self) -> None:
        """`X-Forwarded-For` ist frei wählbar.

        Ihm zu glauben hieße: jeder Angreifer schreibt sich bei jedem Versuch
        eine neue Herkunft hin und hat gar keine Bremse. Dieselbe Regel wie beim
        Tenant-Header — was der Client schickt, entscheidet nichts über ihn.
        """
        scope = {
            "client": ("10.0.0.7", 51234),
            "headers": [(b"x-forwarded-for", b"1.2.3.4")],
        }

        assert client_ip(scope, trust_forwarded_for=False) == "10.0.0.7"

    def test_it_reads_the_header_only_where_a_proxy_was_declared(self) -> None:
        scope = {
            "client": ("10.0.0.7", 51234),
            "headers": [(b"x-forwarded-for", b"1.2.3.4, 10.0.0.7")],
        }

        # Der linkeste Eintrag ist der ursprüngliche Client.
        assert client_ip(scope, trust_forwarded_for=True) == "1.2.3.4"

    def test_a_request_without_a_peer_gets_one_shared_bucket(self) -> None:
        # Kein Peer heißt: in-process aufgerufen (Tests, ASGI-Transport). Ein
        # eigener Eimer wäre eine Erfindung; ein gemeinsamer ist ehrlich.
        assert client_ip({"client": None, "headers": []}, trust_forwarded_for=False) == "unknown"


def _app(**kwargs: Any) -> FastAPI:
    app = FastAPI()

    @app.post("/auth/login")
    async def login() -> dict[str, str]:
        return {"ok": "yes"}

    @app.post("/auth/logout")
    async def logout() -> dict[str, str]:
        return {"ok": "yes"}

    app.add_middleware(ThrottleMiddleware, **kwargs)
    return app


class TestMiddleware:
    def test_it_brakes_the_named_route_and_leaves_the_others_alone(self) -> None:
        app = _app(limits={("POST", "/auth/login"): Limit(times=2, seconds=60)})
        client = TestClient(app)

        assert [client.post("/auth/login").status_code for _ in range(3)] == [200, 200, 429]
        # Abmelden ist kein Ratespiel und bleibt frei.
        assert client.post("/auth/logout").status_code == 200

    def test_the_refusal_says_when_to_come_back(self) -> None:
        app = _app(limits={("POST", "/auth/login"): Limit(times=1, seconds=60)})
        client = TestClient(app)
        client.post("/auth/login")

        refused = client.post("/auth/login")

        assert refused.status_code == 429
        assert int(refused.headers["Retry-After"]) > 0
        assert refused.headers["content-type"].startswith("application/problem+json")

    def test_the_refusal_says_nothing_about_the_account(self) -> None:
        """Die Bremse darf kein Orakel werden.

        Sie zählt je HERKUNFT, nie je Adresse. Eine Bremse je Adresse wäre
        gleich zweimal falsch: sie verriete, dass es die Adresse gibt, und ein
        Fremder könnte damit eine bestimmte Person aussperren.
        """
        app = _app(limits={("POST", "/auth/login"): Limit(times=1, seconds=60)})
        client = TestClient(app)
        client.post("/auth/login", json={"email": "wer@example.com"})

        refused = client.post("/auth/login", json={"email": "wer@example.com"})

        assert "example.com" not in refused.text
        assert "wer" not in refused.text

    @pytest.mark.parametrize("method", ["GET", "OPTIONS"])
    def test_it_only_looks_at_the_method_it_was_given(self, method: str) -> None:
        # Sonst würde der CORS-Preflight das Budget der Anmeldung verbrauchen,
        # und der Browser sperrte sich mit seiner eigenen Vorabfrage aus.
        app = _app(limits={("POST", "/auth/login"): Limit(times=1, seconds=60)})
        client = TestClient(app)

        for _ in range(5):
            assert client.request(method, "/auth/login").status_code != 429
