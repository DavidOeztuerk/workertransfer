"""Smoke test for worker-cli (Phase 1.5).

The CLI was already repaired and shell-smoke-tested in Phase 1.3 (``worker --help``
shows all 12 commands). This pytest smoke drives the Typer ``app`` object via
``typer.testing.CliRunner`` against ``--help`` — no subprocess, no filesystem
write — and asserts the help lists a known command.
"""

from typer.testing import CliRunner
from worker_cli import app


def test_smoke_cli_help_lists_commands() -> None:
    runner = CliRunner()
    result = runner.invoke(app, ["--help"])

    assert result.exit_code == 0
    # ``new-service`` is one of the 12 registered commands.
    assert "new-service" in result.stdout


def test_migrate_reports_missing_alembic_ini() -> None:
    """A service without apps/<service>/alembic.ini must fail fast with an
    actionable ADR-0010 message (Sub-step 2.2 Task 5), not a bare alembic failure."""
    runner = CliRunner()
    # "nonexistent-service" has no directory at all, so the pre-check fires before
    # any subprocess runs. (apps/identity-service gains alembic.ini in Sub-step 2.4;
    # using a bogus name keeps the test independent of migration scaffolding order.)
    result = runner.invoke(app, ["migrate", "msg", "--service", "nonexistent-service"])

    assert result.exit_code != 0
    out = result.stdout
    assert "alembic.ini" in out
