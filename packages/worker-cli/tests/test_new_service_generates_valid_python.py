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
