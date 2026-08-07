r"""Docker-availability guard for the integration suite (ADR-0011).

A single shared helper imported by both the conftest module-level skip guard
and the individual test-module ``pytestmark`` skipif decorators, so we do
not depend on cross-package ``from tests.integration.conftest import ...``
(which would require a ``tests/__init__.py`` root package and break the
Phase-1 collection convention of unique test filenames with no
``tests/__init__.py``).
"""

from __future__ import annotations

import shutil
import socket
import subprocess


def _docker_daemon_up() -> bool:
    # 2375 is rarely open unencrypted; the authoritative check is `docker info`.
    try:
        socket.create_connection(("127.0.0.1", 2375), timeout=0.2)
    except OSError:
        pass
    r = subprocess.run(["docker", "info"], capture_output=True, text=True, timeout=15)
    return r.returncode == 0


def _docker_available() -> bool:
    return shutil.which("docker") is not None and _docker_daemon_up()
