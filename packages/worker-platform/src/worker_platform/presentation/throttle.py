"""Eine Bremse am Auth-Rand — gegen Durchprobieren, nicht gegen Benutzung.

Sie schließt den einzigen `TODO`-Marker, der im Code stand: der Anmeldeweg
hatte nichts, was wiederholtes Raten verlangsamt.

**Warum nicht `worker-ratelimit`.** Das Paket verlangt Redis, und Redis steht
nicht im Stack — es hat bis heute keinen Konsumenten. Es hätte hier also eine
Infrastruktur eingeführt, die nur für diese eine Bremse da wäre. Dazu zählt es
den *abgelehnten* Versuch mit, wodurch sich eine Sperre selbst am Leben hält:
wer dagegenläuft, kommt nicht wieder herein, solange er es weiter versucht.
Dieselbe Lehre wie ADR-0021: eine Umsetzung, die wirklich läuft, ist mehr wert
als drei, die niemand einschaltet. Der Port bleibt die Naht — ein geteilter
Zähler kann später dahinter, wenn eine Umgebung mehrere Instanzen hat.

**Je Herkunft, nie je Adresse.** Eine Bremse je E-Mail-Adresse wäre zweimal
falsch: sie verriete durch ihr Verhalten, dass es die Adresse gibt, und ein
Fremder könnte damit eine bestimmte Person aussperren.
"""

from __future__ import annotations

import time
from collections import deque
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from typing import Any

from starlette.types import ASGIApp, Message, Receive, Scope, Send

from worker_platform.context import get_correlation_id

__all__ = [
    "Decision",
    "Limit",
    "SlidingWindowLimiter",
    "ThrottleMiddleware",
    "client_ip",
]


@dataclass(frozen=True, slots=True)
class Limit:
    """So viele Versuche in so vielen Sekunden."""

    times: int
    seconds: int


@dataclass(frozen=True, slots=True)
class Decision:
    allowed: bool
    #: Sekunden bis zum nächsten freien Versuch; 0, wenn es einen gibt.
    retry_after: int


class SlidingWindowLimiter:
    """Ein gleitendes Fenster je Schlüssel, im Prozess.

    Gleitend und nicht „je angefangene Minute": ein Fenster, das zur vollen
    Minute aufmacht, lädt dazu ein, im Takt weiterzuprobieren. Hier fällt jeder
    einzelne Versuch für sich wieder aus dem Fenster.

    Der abgelehnte Versuch wird **nicht** mitgezählt — sonst hielte sich die
    Sperre selbst am Leben.
    """

    def __init__(self, *, clock: Callable[[], float] = time.monotonic) -> None:
        self._clock = clock
        self._seen: dict[str, deque[float]] = {}

    @property
    def tracked_keys(self) -> int:
        """Wie viele Schlüssel gerade Platz belegen — für den Speicher-Test."""
        return len(self._seen)

    def hit(self, key: str, limit: Limit) -> Decision:
        now = self._clock()
        cutoff = now - limit.seconds
        self._forget_expired(cutoff)

        attempts = self._seen.setdefault(key, deque())
        while attempts and attempts[0] <= cutoff:
            attempts.popleft()

        if len(attempts) >= limit.times:
            # Nur lesen, nicht schreiben: siehe Klassendoku.
            oldest = attempts[0]
            return Decision(allowed=False, retry_after=max(1, int(oldest + limit.seconds - now)))

        attempts.append(now)
        return Decision(allowed=True, retry_after=0)

    def _forget_expired(self, cutoff: float) -> None:
        """Aufräumen, damit die Bremse nicht selbst zum Speicherleck wird.

        Ohne das würde jemand mit vielen Herkünften eine Tabelle füllen, die nie
        wieder kleiner wird — eine Bremse, die man gegen den Dienst benutzen
        kann, ist keine.
        """
        stale = [key for key, seen in self._seen.items() if not seen or seen[-1] <= cutoff]
        for key in stale:
            del self._seen[key]


def client_ip(scope: Mapping[str, Any], *, trust_forwarded_for: bool) -> str:
    """Die Herkunft — aus der Verbindung, nicht aus einem Header.

    `X-Forwarded-For` ist frei wählbar. Ihm ungefragt zu glauben hieße: jeder
    Angreifer schreibt sich bei jedem Versuch eine neue Herkunft hin und hat
    damit gar keine Bremse. Dieselbe Regel wie beim Tenant-Header — was der
    Client schickt, entscheidet nichts über ihn, solange die Umgebung nicht
    ausdrücklich sagt, dass ein Proxy davorsteht.
    """
    if trust_forwarded_for:
        headers: list[tuple[bytes, bytes]] = list(scope.get("headers", []))
        for name, value in headers:
            if name == b"x-forwarded-for":
                # Der linkeste Eintrag ist der ursprüngliche Client.
                first = value.decode("latin-1").split(",")[0].strip()
                if first:
                    return first
    client = scope.get("client")
    if client is None:
        # Kein Peer heißt: im selben Prozess aufgerufen. Ein erfundener eigener
        # Eimer wäre gelogen; ein gemeinsamer ist ehrlich.
        return "unknown"
    return str(client[0])


class ThrottleMiddleware:
    """Rohes ASGI, wie die übrigen Middlewares dieses Pakets.

    Sie kennt eine ausdrückliche Zuordnung (Methode, Pfad) → Grenze. Kein
    Muster, keine Voreinstellung für alles: eine Bremse, die versehentlich auf
    einem Lese-Endpunkt landet, merkt man erst im Betrieb.
    """

    def __init__(
        self,
        app: ASGIApp,
        *,
        limits: Mapping[tuple[str, str], Limit],
        limiter: SlidingWindowLimiter | None = None,
        trust_forwarded_for: bool = False,
    ) -> None:
        self._app = app
        self._limits = dict(limits)
        self._limiter = limiter or SlidingWindowLimiter()
        self._trust_forwarded_for = trust_forwarded_for

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self._app(scope, receive, send)
            return

        limit = self._limits.get((scope.get("method", ""), scope.get("path", "")))
        if limit is None:
            await self._app(scope, receive, send)
            return

        origin = client_ip(scope, trust_forwarded_for=self._trust_forwarded_for)
        decision = self._limiter.hit(f"{scope['method']} {scope['path']}|{origin}", limit)
        if decision.allowed:
            await self._app(scope, receive, send)
            return

        await self._refuse(decision, send)

    async def _refuse(self, decision: Decision, send: Send) -> None:
        """Sagt, dass zu oft gefragt wurde — und sonst nichts.

        Kein Wort über das Konto, die Adresse oder den Grund. Die Antwort ist
        dieselbe, ob es die Adresse gibt oder nicht.
        """
        correlation_id = get_correlation_id()
        body: dict[str, Any] = {
            "type": "https://workertransfer.dev/problems/429",
            "title": "Too Many Requests",
            "status": 429,
            "detail": "Zu viele Versuche. Bitte später erneut probieren.",
        }
        if correlation_id is not None:
            body["correlationId"] = correlation_id

        import json

        payload = json.dumps(body).encode()
        start: Message = {
            "type": "http.response.start",
            "status": 429,
            "headers": [
                (b"content-type", b"application/problem+json"),
                (b"content-length", str(len(payload)).encode()),
                (b"retry-after", str(decision.retry_after).encode()),
            ],
        }
        await send(start)
        await send({"type": "http.response.body", "body": payload})
