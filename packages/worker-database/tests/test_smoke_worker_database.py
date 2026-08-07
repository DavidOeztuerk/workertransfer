"""Smoke tests for worker-database (Phase 1.5).

Exercises the declarative ``Base`` and the ``UnitOfWork`` constructor (stores a
session factory, ``_session = None``; does NOT connect). ``create_engine(url)``
is NOT called — it builds a real ``AsyncEngine`` pointed at a database.
"""

from worker_database import Base, UnitOfWork


def test_smoke_base_and_unit_of_work() -> None:
    base = Base()
    unit = UnitOfWork(session_factory=None)

    assert isinstance(base, Base)
    assert unit._session is None
