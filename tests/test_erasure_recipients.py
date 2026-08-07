"""Wer personenbezogene Zeilen hält, MUSS Empfänger der Löschung sein (ADR-0027 §4).

Der Fehler, den dieser Test verhindert, passiert nicht durch eine falsche
Entscheidung, sondern durch **Wegsehen**: irgendwann bekommt ein Dienst eine
neue Tabelle mit einer `subject_id`, niemand denkt an die Kaskade, und die
Löschung ist wieder eine Zusage, die nur zum Teil eingelöst wird. Es fällt nicht
auf, weil nichts rot wird — genau das Muster, das ROADMAP 10.5 beschreibt.

Dieselbe Bauart wie `test_workspace_dependencies.py` und
`test_skill_limits_align.py`: **die Regel bewacht sich selbst**, statt in einer
Datei zu stehen, die niemand liest.

Geprüft wird an der Metadata, nicht am Quelltext: ein regulärer Ausdruck über
`models.py` würde eine Spalte übersehen, die aus einem Mixin oder aus
`build_outbox_table` kommt — und die Outbox ist genau so ein Fall.
"""

from __future__ import annotations

import importlib
from pathlib import Path

import pytest
from identity_service.application.erasure import RECIPIENTS

_REPO = Path(__file__).resolve().parents[1]
_SERVICES = sorted(
    path.name
    for path in (_REPO / "apps").iterdir()
    if (path / "src").is_dir() and path.name != "web"
)

#: Spalten, die eine Zeile an einen **Menschen** binden. `tenant_id` steht
#: bewusst nicht dabei: ein Tenant ist ein Unternehmen, und ein Unternehmen ist
#: keine natürliche Person (ADR-0017).
_PERSONAL_KEYS = frozenset({"subject_id", "user_id"})

#: identity-service ist Empfänger wie die anderen — und zwar zuletzt (§4).
_ERASURE_RECIPIENTS = frozenset(RECIPIENTS) | {"identity"}

#: Dienste, die ausdrücklich **keine** Empfänger sind, weil sie nichts
#: Personenbezogenes halten. Ein Löschbefehl an sie wäre ein Endpunkt, der
#: „erledigt" sagt, ohne je etwas zu tun.
_NOT_RECIPIENTS = frozenset({"jobs", "companies"})


def _short(service_dir: str) -> str:
    return service_dir.removesuffix("-service")


def _personal_tables(service_dir: str) -> dict[str, list[str]]:
    module = importlib.import_module(
        f"{service_dir.replace('-', '_')}.infrastructure.database.models"
    )
    found: dict[str, list[str]] = {}
    for name, table in module.Base.metadata.tables.items():
        keys = sorted(_PERSONAL_KEYS & set(table.c.keys()))
        if keys:
            found[name] = keys
    return found


def test_every_recipient_is_a_real_service() -> None:
    """Ein Tippfehler in der Liste wäre eine Löschung, die sich selbst für
    erledigt erklärt — der Zusteller kennt den Empfänger dann nicht."""
    existing = {_short(name) for name in _SERVICES}

    assert _ERASURE_RECIPIENTS <= existing


@pytest.mark.parametrize("service", _SERVICES)
def test_a_service_holding_personal_rows_is_a_recipient(service: str) -> None:
    personal = _personal_tables(service)
    short = _short(service)

    if short in _ERASURE_RECIPIENTS:
        return

    assert not personal, (
        f"{service} hält personenbezogene Zeilen ({personal}), steht aber nicht in "
        "identity_service.application.erasure.RECIPIENTS. Eine Löschung würde sie "
        "übersehen — und die Plattform verspräche wieder etwas, das nicht passiert "
        "(ADR-0027 §4). Entweder den Dienst in die Empfängerliste aufnehmen UND ihm "
        "einen `/internal/erasure`-Endpunkt geben, oder die Spalte nicht einführen."
    )


@pytest.mark.parametrize("service", sorted(_NOT_RECIPIENTS))
def test_the_services_we_deliberately_left_out_still_hold_nothing_personal(
    service: str,
) -> None:
    """Die Gegenrichtung, und sie ist die wahrscheinlichere.

    `jobs` und `companies` sind heute frei von Personenbezug — eine
    Stellenanzeige und ein Firmenprofil sind Texte eines Unternehmens. Bekäme
    eines von beiden ein `created_by`, wäre die Begründung „ist kein Empfänger"
    von einem Tag auf den anderen falsch, ohne dass jemand sie widerrufen hätte.
    """
    assert _personal_tables(f"{service}-service") == {}


def test_every_recipient_actually_offers_the_endpoint() -> None:
    """Eine Empfängerliste, die auf einen Dienst ohne Endpunkt zeigt, wäre eine
    Kaskade, die ewig gegen ein 404 läuft."""
    missing = []
    for short in sorted(RECIPIENTS):
        module = f"{short}_service".replace("-", "_")
        try:
            importlib.import_module(f"{module}.presentation.http.erasure_router")
        except ModuleNotFoundError:
            missing.append(short)

    assert not missing, f"Empfänger ohne `/internal/erasure`: {missing}"
