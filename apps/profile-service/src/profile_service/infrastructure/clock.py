"""Systemuhr — die Produktionsfassung des Clock-Ports."""

from __future__ import annotations

from datetime import datetime

from worker_shared import utc_now

__all__ = ["SystemClock"]


class SystemClock:
    def now(self) -> datetime:
        # utc_now ist zeitzonenbewusst; datetime.utcnow() ist es nicht und
        # vergleicht sich falsch gegen timestamptz-Spalten.
        return utc_now()
