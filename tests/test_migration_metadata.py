"""Alembic muss auf DIE Metadata zeigen, auf der die Modelle liegen.

Der Fehler, den dieser Test findet, ist still und teuer: `env.py` setzte
`target_metadata = worker_database.Base.metadata`, während die Modelle des
Dienstes auf seiner **eigenen** `Base` liegen (ADR-0016). Diese Metadata ist
dann leer — und `alembic revision --autogenerate` erzeugt folgerichtig eine
Migration, die **jede Tabelle löscht**.

`alembic upgrade head` merkt davon nichts; es führt nur die vorhandenen Skripte
aus. Deshalb lief der Stack jahrelang grün über einem Generator, der beim
ersten `--autogenerate` ein Datengrab ausgehoben hätte.

Geprüft wird jeder Dienst, nicht nur der Generator: der Fehler stand in acht von
zehn `env.py`, weil sie alle aus derselben Vorlage stammen.
"""

from __future__ import annotations

import importlib
import re
from pathlib import Path

import pytest

_REPO = Path(__file__).resolve().parents[1]
_SERVICES = sorted(path for path in (_REPO / "apps").iterdir() if (path / "migrations").is_dir())


def _module_name(service_dir: Path) -> str:
    return service_dir.name.replace("-", "_")


@pytest.mark.parametrize("service", _SERVICES, ids=lambda path: path.name)
def test_alembic_sees_the_tables_the_service_actually_defines(service: Path) -> None:
    module = _module_name(service)
    env = (service / "migrations" / "env.py").read_text(encoding="utf-8")

    # Woher `env.py` seine `Base` holt — genau die Zeile, die falsch war.
    imported_from = re.search(r"^from (\S+) import Base$", env, re.MULTILINE)
    assert imported_from is not None, f"{service.name}: env.py importiert keine Base"
    source = imported_from.group(1)

    models = importlib.import_module(f"{module}.infrastructure.database.models")
    alembic_base = importlib.import_module(source).Base
    # `models.Base` IST die Base, auf der die Modelle dieses Dienstes liegen —
    # sie steht durch den Import im Namensraum des Modells-Moduls.
    models_base = models.Base

    # Auf Identität, nicht auf „nicht leer": geprüft wird, ob es DIESELBE Base
    # ist. Ein Test auf „hat Tabellen" wäre reihenfolgeabhängig — sobald
    # irgendein anderer Dienst seine Modelle auf `worker_database.Base`
    # registriert hat, sähe die geteilte Metadata voll aus, und der Fehler
    # verschwände je nachdem, welche Testdatei zuerst lief. Genau das ist beim
    # ersten Entwurf dieses Tests passiert.
    assert alembic_base is models_base, (
        f"{service.name}: env.py zeigt `target_metadata` auf {source!r}, "
        f"die Modelle liegen aber auf {models_base.__module__!r}. "
        "Autogenerate sähe keine einzige eigene Tabelle und würde daraus eine "
        "Migration bauen, die jede Tabelle löscht."
    )
