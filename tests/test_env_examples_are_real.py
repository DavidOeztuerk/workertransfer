"""Eine `.env.example`, die Variablen nennt, die niemand liest, ist schlimmer als keine.

`extra="ignore"` heißt: pydantic-settings verwirft eine unbekannte Variable
**stillschweigend**. Wer aus so einer Vorlage eine echte Umgebung baut, setzt
`WORKER_REDIS_URL`, `WORKER_JWT_ALGORITHM=RS256` oder
`WORKER_FEATURE_RATE_LIMITING=true` — und nichts davon tut irgendetwas. Es gibt
keine Fehlermeldung, keine Warnung, nichts. Man merkt es erst, wenn man sich
darauf verlassen hat.

Genau so sahen alle acht generierten `.env.example` aus: rund fünfzehn
Variablen für Redis, RabbitMQ, OpenTelemetry, Feature-Flags und einen
JWT-Algorithmus, den dieses System nicht benutzt (es signiert HS256, nicht
RS256). Keine einzige davon wird von einer Settings-Klasse gelesen.

Dieser Test hält jede Vorlage gegen die Klasse, die sie beschreibt.
"""

from __future__ import annotations

import importlib
import re
from pathlib import Path

import pytest
from pydantic_settings import BaseSettings

_REPO = Path(__file__).resolve().parents[1]
_EXAMPLES = sorted(path for path in (_REPO / "apps").glob("*/.env.example") if path.is_file())


def _settings_class(service_dir: Path) -> type[BaseSettings]:
    module_name = service_dir.name.replace("-", "_")
    module = importlib.import_module(f"{module_name}.configuration")
    for attribute in vars(module).values():
        if (
            isinstance(attribute, type)
            and issubclass(attribute, BaseSettings)
            and attribute.__module__ == module.__name__
        ):
            return attribute
    raise AssertionError(f"{service_dir.name}: keine Settings-Klasse gefunden")


def _declared(example: Path) -> set[str]:
    names = set()
    for line in example.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        match = re.match(r"^([A-Z0-9_]+)=", stripped)
        if match:
            names.add(match.group(1))
    return names


@pytest.mark.parametrize("example", _EXAMPLES, ids=lambda path: path.parent.name)
def test_every_variable_in_the_example_is_actually_read(example: Path) -> None:
    service_dir = example.parent
    settings = _settings_class(service_dir)
    # pydantic-settings liest `WORKER_` + Feldname, ohne Rücksicht auf
    # Groß-/Kleinschreibung.
    known = {f"WORKER_{name.upper()}" for name in settings.model_fields}

    unread = sorted(name for name in _declared(example) if name not in known)

    assert not unread, (
        f"{service_dir.name}/.env.example nennt Variablen, die keine "
        f'Settings-Klasse liest: {unread}. `extra="ignore"` verwirft sie '
        "stillschweigend — wer daraus eine echte Umgebung baut, glaubt, etwas "
        "eingestellt zu haben."
    )


@pytest.mark.parametrize("example", _EXAMPLES, ids=lambda path: path.parent.name)
def test_every_setting_the_service_reads_is_in_the_example(example: Path) -> None:
    """Die Gegenrichtung — und sie fehlte, bis sie etwas übersah.

    Der Test darüber prüft: was in der Vorlage steht, wird auch gelesen. Das
    allein reicht nicht. Als `jobs-service` die Formulierungshilfe bekam, las
    es drei neue Variablen, von denen keine in seiner `.env.example` stand —
    und `profile-service` fehlte `WORKER_DRAFTING_BASE_URL`. Beide Dateien
    behaupten in ihrer eigenen Kopfzeile, die Variablen zu nennen, die der
    Dienst **WIRKLICH liest**; damit waren beide unwahr.

    Der Schaden ist der spiegelverkehrte zum anderen Test und genauso still:
    dort stellt man etwas ein, das nichts tut, hier weiß man nicht, dass man
    etwas einstellen KANN. Wer aus der Vorlage eine echte Umgebung baut,
    bekommt dann eine Voreinstellung, die er nie gewählt hat — bei
    `WORKER_ANTHROPIC_API_KEY` wäre das „aus", was gutgeht, bei einer künftigen
    Einstellung mit unglücklicher Voreinstellung nicht.
    """
    service_dir = example.parent
    settings = _settings_class(service_dir)
    declared = _declared(example)

    missing = sorted(
        f"WORKER_{name.upper()}"
        for name in settings.model_fields
        if f"WORKER_{name.upper()}" not in declared
    )

    assert not missing, (
        f"{service_dir.name}/.env.example nennt Einstellungen NICHT, die "
        f"{settings.__name__} liest: {missing}. Die Datei sagt in ihrer "
        "Kopfzeile, sie liste, was der Dienst wirklich liest — wer daraus eine "
        "echte Umgebung baut, erfährt sonst nie, dass es diese Stellschrauben "
        "gibt."
    )


def test_the_root_example_documents_only_settings_that_exist() -> None:
    """Auch die Wurzel-Vorlage, aus zwei Gründen.

    Erstens ist sie die Datei, die jemand zuerst liest. Zweitens ist genau
    dort der Fehler passiert, den dieser Test verhindert: `WORKER_DRAFTING_BASE_URL`
    stand darin, bevor es die Einstellung gab — eine dokumentierte Fiktion in
    der Datei, die Fiktion verhindern soll.

    Geprüft werden nur `WORKER_`-Variablen. `ANTHROPIC_API_KEY` und
    `VITE_API_BASE_URL` tragen den Präfix bewusst nicht: die eine liest
    docker-compose (und reicht sie als `WORKER_ANTHROPIC_API_KEY` weiter), die
    andere backt Vite in das Bündel.
    """
    known: set[str] = set()
    for example in _EXAMPLES:
        known |= {f"WORKER_{name.upper()}" for name in _settings_class(example.parent).model_fields}
    # identity-service hat keine .env.example, seine Felder gehören aber dazu.
    from identity_service.configuration import IdentityServiceSettings

    known |= {f"WORKER_{name.upper()}" for name in IdentityServiceSettings.model_fields}

    declared = {name for name in _declared(_REPO / ".env.example") if name.startswith("WORKER_")}
    unread = sorted(declared - known)

    assert not unread, f".env.example nennt Variablen, die keine Settings-Klasse liest: {unread}"
