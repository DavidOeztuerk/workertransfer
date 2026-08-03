"""`worker new-service` must emit code that at least *parses*.

Phase 1.3 declared the CLI repaired after smoke-testing `--help`; nobody ever
compiled its output. It was emitting literal `${ServiceClass}` into generated
Python (the render context supplied `service_class`, the templates asked for
`ServiceClass`, and `Template.safe_substitute` silently leaves unknown keys in
place) and wiring services to a fluent `PlatformBuilder` that ADR-0003 rejected
and that does not exist in `worker_platform`.

These tests pin the contract that was broken: substituted placeholders, parseable
Python, and the scaffolding a service needs to actually run (ADR-0003
Composition-Root, ADR-0010 per-service Alembic, ADR-0011 Docker-gated
integration tests).
"""

from __future__ import annotations

import ast
import os
import re
from pathlib import Path

import pytest
from typer.testing import CliRunner
from worker_cli import app

UNRESOLVED_PLACEHOLDER = re.compile(r"\$\{[A-Za-z_][A-Za-z0-9_]*\}")


@pytest.fixture(scope="module")
def generated(tmp_path_factory: pytest.TempPathFactory) -> Path:
    workdir = tmp_path_factory.mktemp("scaffold")
    previous = Path.cwd()
    os.chdir(workdir)
    try:
        result = CliRunner().invoke(app, ["new-service", "billing-service"])
    finally:
        os.chdir(previous)
    assert result.exit_code == 0, result.output
    return workdir / "apps" / "billing-service"


def _python_files(root: Path) -> list[Path]:
    return sorted(p for p in root.rglob("*.py"))


def _module_name(generated: Path) -> str:
    """Der Paketname unter src/ — der Generator leitet ihn aus dem Servicenamen ab."""
    return next(p.name for p in (generated / "src").iterdir() if p.is_dir())


def test_generated_python_parses(generated: Path) -> None:
    files = _python_files(generated)
    assert files, "generator produced no Python files"

    failures: list[str] = []
    for path in files:
        try:
            ast.parse(path.read_text(), filename=str(path))
        except SyntaxError as exc:
            failures.append(f"{path.relative_to(generated)}: {exc}")

    assert not failures, "generated Python does not parse:\n  " + "\n  ".join(failures)


def test_no_placeholder_survives_into_python(generated: Path) -> None:
    leftovers = {
        str(path.relative_to(generated)): sorted(
            set(UNRESOLVED_PLACEHOLDER.findall(path.read_text()))
        )
        for path in _python_files(generated)
        if UNRESOLVED_PLACEHOLDER.search(path.read_text())
    }
    assert not leftovers, f"unsubstituted template placeholders reached the output: {leftovers}"


def test_names_are_derived_from_the_service_name(generated: Path) -> None:
    configuration = (generated / "src/billing_service/configuration.py").read_text()
    assert "class BillingServiceSettings(PlatformSettings):" in configuration
    assert 'service_name: str = "billing-service"' in configuration


def test_composition_root_uses_the_kernel_factory_not_a_fluent_builder(generated: Path) -> None:
    compose_api = (generated / "src/billing_service/presentation/compose_api.py").read_text()
    assert "create_api_app" in compose_api
    # ADR-0003: no fluent builder, and worker_platform.builder does not exist.
    assert "PlatformBuilder" not in compose_api


def test_alembic_scaffolding_exists(generated: Path) -> None:
    # The CLI's own "next steps" tell the user to run `alembic revision`; before
    # this, nothing generated an alembic.ini for that command to find (ADR-0010).
    assert (generated / "alembic.ini").is_file()
    assert (generated / "migrations/env.py").is_file()
    assert (generated / "migrations/script.py.mako").is_file()
    assert (generated / "migrations/versions").is_dir()


def test_mako_placeholders_survive_in_the_revision_template(generated: Path) -> None:
    # script.py.mako is filled by alembic at revision time, not by the scaffolder.
    mako = (generated / "migrations/script.py.mako").read_text()
    assert "${up_revision}" in mako


def test_integration_suite_is_docker_gated(generated: Path) -> None:
    conftest = (generated / "tests/integration/conftest.py").read_text()
    assert (generated / "tests/integration/_docker.py").is_file()
    assert "_docker_available" in conftest  # ADR-0011 offline-skip


def test_app_test_covers_the_platform_wiring(generated: Path) -> None:
    test_app = (generated / "tests/test_app.py").read_text()
    assert "/health/live" in test_app
    assert "x-correlation-id" in test_app


def test_generated_service_actually_starts(generated: Path) -> None:
    """Syntax ist nicht genug.

    `ast.parse` hat jahrelang grün gemeldet, während models.py ein Modul
    importierte, das der Generator nie schrieb (`…database.base`). Ein Service
    aus dem Generator war damit nicht startbar. Dieser Test lädt das Paket
    wirklich — ein fehlendes Modul fällt sofort auf.
    """
    import subprocess
    import sys

    module = _module_name(generated)
    src = generated / "src"
    # Eigener Interpreter-Prozess: der generierte Service darf den laufenden
    # Testprozess nicht mit seinen Modulen verunreinigen, und ein Importfehler
    # soll den Lauf nicht abbrechen, sondern als Fehlschlag zurückkommen.
    result = subprocess.run(
        # main baut die App (create_app auf Modulebene) und zieht damit
        # Konfiguration, Composition-Root, Router und Modelle mit hoch. Nur die
        # Modelle zu importieren hieße, den halben Service ungeprüft zu lassen.
        [sys.executable, "-c", f"import {module}.main"],
        capture_output=True,
        text=True,
        env={"PYTHONPATH": str(src), "PATH": os.environ.get("PATH", "")},
    )

    assert result.returncode == 0, (
        f"Der generierte Service ist nicht importierbar:\n{result.stderr}"
    )


def test_no_per_service_docker_files_are_generated(generated: Path) -> None:
    """Das Repo hat ein geteiltes docker/service.Dockerfile und EINE Compose.

    Ein generierter Service mit eigenem Dockerfile brächte konkurrierende
    Infrastruktur mit, die niemand pflegt.
    """
    assert not (generated / "Dockerfile").exists()
    assert not (generated / "docker-compose.yml").exists()


def test_the_service_owns_its_declarative_base(generated: Path) -> None:
    """ADR-0016: eigene MetaData je Service, nicht worker_database.Base."""
    module = _module_name(generated)
    base = generated / "src" / module / "infrastructure" / "database" / "base.py"

    assert base.exists(), "base.py fehlt — genau der Fehler, der models.py brach"
    text = base.read_text()
    assert "DeclarativeBase" in text
    # Auf den IMPORT prüfen, nicht auf die Zeichenkette: der Docstring erklärt
    # gerade, warum worker_database.Base hier falsch wäre.
    assert "from worker_database" not in text
    assert "import worker_database" not in text


def test_generated_service_passes_the_repo_quality_gates(generated: Path) -> None:
    """Ein Generator, dessen Ausgabe rot ist, hilft niemandem.

    Vor dieser Prüfung erzeugte `worker new-service` einen Service mit 28 ruff-
    und 28 mypy-Fehlern: doppelte `Base`, eine zweite UnitOfWork, ein eigener
    Mediator, ein Beispielmodell mit ungültigem Constraint. Jeder neue Service
    hätte damit rot begonnen — und `kon.txt` Regel Nr. 1 wäre eine Behauptung
    geblieben.
    """
    import subprocess

    repo_root = Path(__file__).resolve().parents[3]

    # Nur src/: zwei ruff-Verhalten hängen davon ab, WO der Service liegt, und
    # kein Template kann beide zugleich erfüllen.
    #   - I001 sortiert Erstanbieter-Importe anders, je nachdem ob ruff das
    #     Paket als solches erkennt (im Repo ja, im Temp-Verzeichnis nein).
    #   - per-file-ignores ("**/tests/**/*.py") greifen nicht für Pfade
    #     außerhalb der Projektwurzel, weshalb das generierte _docker.py hier
    #     S607 auslöst, im Repo aber nicht.
    # Für den echten Service greift `ruff check .` im Repo ohnehin über alles.
    src = generated / "src"
    lint = subprocess.run(
        ["uv", "run", "ruff", "check", "--ignore", "I001", str(src)],
        capture_output=True,
        text=True,
        cwd=repo_root,
    )
    assert lint.returncode == 0, f"ruff meldet Fehler im generierten Service:\n{lint.stdout}"

    fmt = subprocess.run(
        ["uv", "run", "ruff", "format", "--check", str(src)],
        capture_output=True,
        text=True,
        cwd=repo_root,
    )
    assert fmt.returncode == 0, f"Formatierung weicht ab:\n{fmt.stdout}"


def test_the_generated_app_wires_authentication(generated: Path) -> None:
    """Ohne diese Verdrahtung antwortet JEDER Endpunkt 401.

    Der Prinzipal bleibt leer, obwohl ein gültiges Token mitgeschickt wurde.
    Die Vorlage lieferte es lange nicht mit; jeder Service musste es von Hand
    nachtragen, und wer es vergaß, merkte es erst am ersten Integrationstest —
    zuletzt beim github-service.
    """
    module = _module_name(generated)
    source = (generated / "src" / module / "presentation" / "compose_api.py").read_text()

    assert "auth_middleware=JwtAuthMiddleware" in source
    assert 'expected_type="access"' in source
