"""Dauerläufer im Dienst: gestartet mit der App, beendet mit ihr.

Gebraucht für den Outbox-Zusteller (9.1). Die interessanten Zusagen sind nicht
„es läuft", sondern die beiden Fälle, in denen ein naives Lifespan schweigt:
ein Absturz und ein Herunterfahren mitten in der Arbeit.
"""

from __future__ import annotations

import asyncio

from fastapi.testclient import TestClient
from worker_platform.configuration import PlatformSettings
from worker_platform.presentation.app import create_api_app


def _settings() -> PlatformSettings:
    return PlatformSettings(service_name="background-test")


class TestItRuns:
    def test_a_background_runner_starts_with_the_app(self) -> None:
        started = asyncio.Event()

        async def runner() -> None:
            started.set()
            await asyncio.sleep(3600)

        app = create_api_app(_settings(), background=(runner,))
        with TestClient(app) as client:
            assert client.get("/health/live").status_code == 200
            assert started.is_set()

    def test_without_a_runner_nothing_changes(self) -> None:
        app = create_api_app(_settings())
        with TestClient(app) as client:
            assert client.get("/health/live").status_code == 200


class TestItStopsProperly:
    def test_shutdown_waits_for_the_runner_to_actually_finish(self) -> None:
        """Nur `cancel()` zu rufen beendet den Prozess mitten in der Arbeit.

        Für den Outbox-Zusteller hieße das: abgebrochen, während eine
        Transaktion offen ist. Der Test hält fest, dass das Herunterfahren
        wartet, bis die Aufgabe ihr Aufräumen wirklich durchlaufen hat.
        """
        cleaned = []

        async def runner() -> None:
            try:
                await asyncio.sleep(3600)
            except asyncio.CancelledError:
                # Aufräumen, das WIRKLICH Zeit braucht. Mit `sleep(0)` war
                # dieser Test auch ohne das `await` im Lifespan grün — die
                # Aufgabe kam beim Herunterfahren zufällig noch dran. Ein Test,
                # der nicht fehlschlagen kann, bewacht nichts.
                await asyncio.sleep(0.2)
                cleaned.append("fertig")
                raise

        app = create_api_app(_settings(), background=(runner,))
        with TestClient(app) as client:
            client.get("/health/live")

        assert cleaned == ["fertig"]


class TestACrashIsNotSilent:
    def test_a_runner_that_raises_does_not_take_the_app_down(self) -> None:
        async def exploding() -> None:
            raise RuntimeError("Datenbank weg")

        app = create_api_app(_settings(), background=(exploding,))
        with TestClient(app) as client:
            # Die App bedient weiter — ein Zusteller, der stirbt, darf nicht
            # den ganzen Dienst mitnehmen.
            assert client.get("/health/live").status_code == 200

    def test_a_crash_is_logged_because_otherwise_nobody_learns_of_it(self, caplog: object) -> None:
        """Eine Hintergrundaufgabe stirbt sonst LAUTLOS.

        Die App liefe weiter und beantwortete Anfragen, aber es stellte niemand
        mehr zu — genau der Zustand, den die Outbox abschaffen soll.
        """
        import logging

        async def exploding() -> None:
            raise RuntimeError("Datenbank weg")

        app = create_api_app(_settings(), background=(exploding,))
        with caplog.at_level(logging.ERROR):  # type: ignore[attr-defined]
            with TestClient(app) as client:
                client.get("/health/live")

        assert "Hintergrundaufgabe" in caplog.text  # type: ignore[attr-defined]
