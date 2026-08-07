"""Zeit als Abhängigkeit, nicht als Import mitten im Handler."""

from __future__ import annotations

from datetime import datetime

from worker_shared import utc_now

__all__ = ["SystemClock"]


class SystemClock:
    def now(self) -> datetime:
        return utc_now()
