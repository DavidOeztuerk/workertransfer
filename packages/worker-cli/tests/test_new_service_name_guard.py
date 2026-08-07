"""Der Generator nimmt nicht mehr jeden Namen.

Beide Regeln stammen aus einem Fehler, der wirklich passiert ist:
`worker new-service github` erzeugte `apps/github` mit dem Modul `github` — ein
Name, der von jedem anderen Dienst abweicht UND das PyPI-Paket `github`
überschattet. Genau an diesem Import ist `worker-github` zerbrochen (ADR-0022).
"""

from __future__ import annotations

import pytest
import typer
from worker_cli import _reject_bad_service_name


def test_a_conventional_name_passes() -> None:
    # Ein Name, den es noch nicht gibt — genau der Fall, für den der Generator
    # da ist.
    _reject_bad_service_name("matching-service")


def test_a_name_without_the_suffix_is_refused() -> None:
    # Sonst heißt das Modul `github` statt `github_service` — und weicht damit
    # als einziger Dienst von allen anderen ab.
    with pytest.raises(typer.Exit):
        _reject_bad_service_name("github")


@pytest.mark.parametrize("name", ["GitHub-Service", "github_service", "1-service", "-service"])
def test_something_that_is_not_kebab_case_is_refused(name: str) -> None:
    with pytest.raises(typer.Exit):
        _reject_bad_service_name(name)


def test_a_module_that_already_exists_is_refused() -> None:
    """Ein erzeugtes Modul gleichen Namens würde das installierte überschatten.

    `json` gibt es immer — die Standardbibliothek zählt genauso, denn ein
    `import json` irgendwo im Baum träfe danach das falsche. (Der Name scheitert
    ohnehin schon an der fehlenden Endung; geprüft wird hier, dass er nicht
    durchkommt.)
    """
    with pytest.raises(typer.Exit):
        _reject_bad_service_name("json")


def test_an_existing_service_module_is_refused_too() -> None:
    # `transfer_service` ist im Workspace installiert. Ein zweiter Dienst mit
    # demselben Modulnamen wäre nicht importierbar — beide hießen gleich.
    with pytest.raises(typer.Exit):
        _reject_bad_service_name("transfer-service")
