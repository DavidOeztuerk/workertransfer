"""System clock — production implementation of the domain Clock port."""

from __future__ import annotations

from datetime import datetime

from worker_shared import utc_now


class SystemClock:
    def now(self) -> datetime:
        # worker_shared.utc_now is timezone-aware; datetime.utcnow() is not, and
        # a naive value compares wrongly against the timestamptz columns the
        # ledger reads back.
        return utc_now()
