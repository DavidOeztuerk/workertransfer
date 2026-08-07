"""Die Uhr als Port: Tests setzen sie, Produktion nimmt die echte."""

from __future__ import annotations

from datetime import UTC, datetime

__all__ = ["SystemClock"]


class SystemClock:
    def now(self) -> datetime:
        return datetime.now(UTC)
