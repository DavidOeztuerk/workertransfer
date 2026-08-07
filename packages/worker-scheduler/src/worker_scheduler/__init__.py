"""Job scheduling: Cron, Recurring, Delayed, Distributed scheduler."""

from __future__ import annotations

from collections.abc import Callable
from datetime import datetime
from typing import Any

from apscheduler.executors.asyncio import AsyncIOExecutor
from apscheduler.jobstores.redis import RedisJobStore
from apscheduler.schedulers.asyncio import AsyncIOScheduler
from apscheduler.triggers.cron import CronTrigger
from apscheduler.triggers.date import DateTrigger
from apscheduler.triggers.interval import IntervalTrigger


class Scheduler:
    def __init__(self, redis_url: str = "redis://localhost:6379/0"):
        jobstores = {
            "default": RedisJobStore(
                jobs_key="apscheduler.jobs", run_times_key="apscheduler.run_times", redis=redis_url
            )
        }
        executors = {"default": AsyncIOExecutor()}
        self._scheduler = AsyncIOScheduler(jobstores=jobstores, executors=executors)

    def start(self) -> None:
        self._scheduler.start()

    def stop(self) -> None:
        self._scheduler.shutdown()

    def add_cron_job(
        self,
        func: Callable[..., Any],
        cron: str,
        args: tuple[Any, ...] = (),
        kwargs: dict[str, Any] | None = None,
        id: str | None = None,
    ) -> str:
        job = self._scheduler.add_job(
            func, CronTrigger.from_crontab(cron), args=args, kwargs=kwargs or {}, id=id
        )
        return str(job.id)

    def add_interval_job(
        self,
        func: Callable[..., Any],
        seconds: int,
        args: tuple[Any, ...] = (),
        kwargs: dict[str, Any] | None = None,
        id: str | None = None,
    ) -> str:
        job = self._scheduler.add_job(
            func, IntervalTrigger(seconds=seconds), args=args, kwargs=kwargs or {}, id=id
        )
        return str(job.id)

    def add_one_time_job(
        self,
        func: Callable[..., Any],
        run_at: datetime,
        args: tuple[Any, ...] = (),
        kwargs: dict[str, Any] | None = None,
        id: str | None = None,
    ) -> str:
        job = self._scheduler.add_job(
            func, DateTrigger(run_date=run_at), args=args, kwargs=kwargs or {}, id=id
        )
        return str(job.id)

    def remove_job(self, job_id: str) -> None:
        self._scheduler.remove_job(job_id)

    def get_jobs(self) -> list[dict[str, Any]]:
        jobs = []
        for job in self._scheduler.get_jobs():
            jobs.append(
                {
                    "id": job.id,
                    "name": job.name,
                    "next_run": job.next_run_time.isoformat() if job.next_run_time else None,
                    "trigger": str(job.trigger),
                }
            )
        return jobs


# Distributed scheduler - only one instance runs the job
class DistributedScheduler(Scheduler):
    def __init__(self, redis_url: str, lock_prefix: str = "scheduler:lock:"):
        super().__init__(redis_url)
        self._redis_url = redis_url
        self._lock_prefix = lock_prefix

    async def _acquire_lock(self, job_id: str) -> bool:
        import redis.asyncio as redis

        client = redis.from_url(self._redis_url)  # type: ignore[no-untyped-call]
        return bool(await client.set(f"{self._lock_prefix}{job_id}", "1", nx=True, ex=60))

    async def _release_lock(self, job_id: str) -> None:
        import redis.asyncio as redis

        client = redis.from_url(self._redis_url)  # type: ignore[no-untyped-call]
        await client.delete(f"{self._lock_prefix}{job_id}")

    def add_cron_job(
        self,
        func: Callable[..., Any],
        cron: str,
        args: tuple[Any, ...] = (),
        kwargs: dict[str, Any] | None = None,
        id: str | None = None,
    ) -> str:
        original_func = func

        async def wrapped(*a: Any, **kw: Any) -> None:
            if await self._acquire_lock(id or func.__name__):
                try:
                    await original_func(*a, **kw)
                finally:
                    await self._release_lock(id or func.__name__)

        return super().add_cron_job(wrapped, cron, args, kwargs, id)
