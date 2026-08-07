"""CLI entrypoint module.

The `[project.scripts]` console entry ``worker`` resolves ``worker_cli.main:app``.
We keep the command definitions in :mod:`worker_cli` (the package ``__init__``) and
re-export the Typer ``app`` object here so the script target always resolves.
"""

from worker_cli import app

__all__ = ["app"]
