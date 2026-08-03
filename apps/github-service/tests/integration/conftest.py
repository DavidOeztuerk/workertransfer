"""Testcontainers Postgres for the github-service integration suite (ADR-0011).

Session-scoped so one container serves the whole suite. Self-skips when no
Docker daemon is reachable, which keeps `make check` and Docker-less runners
green; GitHub Actions' ubuntu-latest has Docker, so they really run there.
"""

from __future__ import annotations

from collections.abc import Iterator

import pytest

from ._docker import _docker_available

if not _docker_available():  # pragma: no cover - environment dependent
    pytest.skip("Docker not available", allow_module_level=True)

from testcontainers.postgres import PostgresContainer


@pytest.fixture(scope="session")
def postgres_url() -> Iterator[str]:
    with PostgresContainer("postgres:17-alpine", driver="asyncpg") as container:
        yield container.get_connection_url()
