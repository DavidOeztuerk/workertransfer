"""Smoke test for worker-scheduler (Phase 1.5).

``Scheduler`` / ``DistributedScheduler`` constructors RAISE in the current venv
(``TypeError: Redis.__init__() got an unexpected keyword argument 'redis'``) —
an apscheduler/redis-py version skew: the ``RedisJobStore`` passes ``redis=...``
to a modern ``redis-py`` ``Redis.__init__`` that no longer accepts it. Module
import itself is clean (no side effect). The smoke therefore stays at
module-import level. Constructing the scheduler is tracked as a follow-up
deficit until the apscheduler/redis-py versions are reconciled.
"""

import worker_scheduler


def test_smoke_module_imports() -> None:
    assert worker_scheduler is not None
    assert worker_scheduler.Scheduler.__name__ == "Scheduler"
