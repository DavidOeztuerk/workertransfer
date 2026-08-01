"""Every workspace package must declare the siblings it imports.

`uv sync --all-packages` flattens the whole workspace into one virtualenv, so a
package that imports `worker_platform` without declaring it still works locally
and in CI — the dependency is present by accident. It breaks the moment a
service is installed on its own (a container image, `uv pip install
apps/identity-service`), which is exactly how these packages are meant to ship.

This test compares what each package's source actually imports against what its
`[project.dependencies]` promises. `[tool.uv.sources]` alone is NOT enough: it
is a resolution rule telling uv *where* to find a package, not a declaration
that this package needs it.
"""

from __future__ import annotations

import re
import tomllib
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ROOTS = (REPO_ROOT / "packages", REPO_ROOT / "apps")

# `from worker_x import ...` / `import worker_x`, at module level or nested
# inside a function (the lazy-import pattern several packages use).
_IMPORT_RE = re.compile(r"^\s*(?:from|import)\s+(worker_[a-z_]+)", re.MULTILINE)
_REQUIREMENT_NAME_RE = re.compile(r"^([A-Za-z0-9_.-]+)")


def _distribution_name(module: str) -> str:
    """worker_platform -> worker-platform."""
    return module.replace("_", "-")


def _iter_packages() -> list[tuple[str, Path, dict]]:
    found = []
    for root in PACKAGE_ROOTS:
        for directory in sorted(p for p in root.iterdir() if p.is_dir()):
            manifest = directory / "pyproject.toml"
            if not manifest.is_file():
                continue  # apps/web is a pnpm package, not a uv one
            data = tomllib.loads(manifest.read_text())
            if "project" not in data:
                continue
            found.append((data["project"]["name"], directory, data))
    return found


def _declared_dependencies(data: dict) -> set[str]:
    return {
        match.group(1)
        for dep in data["project"].get("dependencies", [])
        if (match := _REQUIREMENT_NAME_RE.match(dep))
    }


def _imported_siblings(directory: Path, own_name: str) -> set[str]:
    imported: set[str] = set()
    for source in directory.rglob("src/**/*.py"):
        for match in _IMPORT_RE.finditer(source.read_text(encoding="utf-8")):
            imported.add(_distribution_name(match.group(1)))
    imported.discard(own_name)
    return imported


def test_every_imported_workspace_sibling_is_declared() -> None:
    offenders: list[str] = []
    for name, directory, data in _iter_packages():
        missing = _imported_siblings(directory, name) - _declared_dependencies(data)
        if missing:
            offenders.append(f"{name} imports {sorted(missing)} but does not declare it")

    assert not offenders, (
        "Workspace packages import siblings they do not declare in "
        "[project.dependencies]. Add them there — a [tool.uv.sources] entry only "
        "tells uv where to resolve a name, it does not install anything:\n  "
        + "\n  ".join(offenders)
    )


def test_uv_sources_entries_resolve_to_real_packages() -> None:
    """A [tool.uv.sources] entry pointing at a non-existent directory is a trap."""
    workspace_dirs = {directory.name for _, directory, _ in _iter_packages()}
    offenders: list[str] = []
    for name, _directory, data in _iter_packages():
        sources = data.get("tool", {}).get("uv", {}).get("sources", {})
        for dependency, spec in sources.items():
            if not (isinstance(spec, dict) and spec.get("workspace")):
                continue
            if dependency not in workspace_dirs:
                offenders.append(f"{name} -> {dependency}")

    # worker-ai and worker-files are excluded from the workspace on purpose
    # (heavy C/ML wheels with no consumer); orphaned references to them are
    # tolerated because uv only resolves a source entry when something depends
    # on that name. Anything else is a typo.
    tolerated = {"worker-ai", "worker-files"}
    unexpected = [o for o in offenders if o.split(" -> ")[1] not in tolerated]
    assert not unexpected, "Unknown [tool.uv.sources] workspace targets:\n  " + "\n  ".join(
        unexpected
    )
