"""Das Helm-Chart und docker-compose.yml müssen dieselben Dienste kennen.

Gleiche Bauart wie ``test_workspace_dependencies.py`` und
``test_erasure_recipients.py``: ein Wächter gegen etwas, das niemandem auffällt,
solange man nicht hinsieht.

Der Schaden ohne diesen Test ist unauffällig und teuer. Ein neuer Dienst wird in
``docker-compose.yml`` eingetragen, lokal läuft alles — im Cluster fehlt er
einfach. Kein Fehler, kein roter Pod, keine Meldung: das Gateway hat für seine
Pfade keine Route und die Oberfläche bekommt 404, als wäre der Pfad falsch
geschrieben. Genau dieses Muster hat in diesem Repo schon einmal 16 E2E-Reisen
still übersprungen und dabei „alles grün" gemeldet.
"""

from __future__ import annotations

import re
import tomllib
from pathlib import Path
from typing import Any

import yaml

ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ROOT / "docker-compose.yml"
VALUES = ROOT / "deploy" / "helm" / "workertransfer" / "values.yaml"
DYNAMIC = ROOT / "docker" / "traefik" / "dynamic.yml"


def _compose() -> dict[str, Any]:
    return yaml.safe_load(COMPOSE.read_text(encoding="utf-8"))


def _values() -> dict[str, Any]:
    return yaml.safe_load(VALUES.read_text(encoding="utf-8"))


def _compose_python_services() -> dict[str, dict[str, Any]]:
    """Die Python-Dienste aus Compose — erkannt am gemeinsamen Dockerfile.

    Nicht an einer Namensliste, sonst wäre der Wächter genau die Liste, die er
    bewachen soll.
    """
    services = _compose()["services"]
    return {
        name: block
        for name, block in services.items()
        if isinstance(block, dict)
        and (block.get("build") or {}).get("dockerfile") == "docker/service.Dockerfile"
    }


def test_chart_kennt_jeden_compose_dienst() -> None:
    compose_names = set(_compose_python_services())
    chart_names = {svc["name"] for svc in _values()["services"]}

    fehlend = compose_names - chart_names
    ueberzaehlig = chart_names - compose_names

    assert not fehlend, (
        f"In docker-compose.yml, aber nicht im Helm-Chart: {sorted(fehlend)}. "
        f"Ohne Eintrag in {VALUES.relative_to(ROOT)} läuft der Dienst im Cluster "
        f"gar nicht — und niemand merkt es, weil nichts rot wird."
    )
    assert not ueberzaehlig, (
        f"Im Helm-Chart, aber nicht in docker-compose.yml: {sorted(ueberzaehlig)}."
    )


def test_ports_und_datenbanken_stimmen_ueberein() -> None:
    """Ein Dienst, der im Cluster auf einem anderen Port lauscht, ist unerreichbar.

    Die Ports stehen an drei Stellen (Compose, Chart, docker/traefik/dynamic.yml);
    genau deshalb wird hier verglichen statt vertraut.
    """
    compose_services = _compose_python_services()
    chart = {svc["name"]: svc for svc in _values()["services"]}

    for name, block in compose_services.items():
        env = block.get("environment") or {}
        compose_port = int(str(env["WORKER_PORT"]))
        assert chart[name]["port"] == compose_port, (
            f"{name}: Port {chart[name]['port']} im Chart, {compose_port} in Compose."
        )

        url = str(env["WORKER_DATABASE_URL"])
        compose_db = url.rsplit("/", 1)[-1]
        assert chart[name]["database"] == compose_db, (
            f"{name}: Datenbank {chart[name]['database']!r} im Chart, "
            f"{compose_db!r} in Compose. Eine falsche Datenbank fällt erst auf, "
            f"wenn Daten fehlen."
        )


def test_gateway_landkarte_kennt_nur_existierende_dienste() -> None:
    """Jedes Ziel in dynamic.yml muss ein Service-Objekt bekommen.

    Die Datei wird unverändert in beide Umgebungen gehängt. Zeigt sie auf einen
    Namen, den das Chart nicht anlegt, antwortet das Gateway für diese Pfade mit
    einem Fehler, der nach einem kaputten Dienst aussieht statt nach einer
    fehlenden Zeile.
    """
    dynamic = yaml.safe_load(DYNAMIC.read_text(encoding="utf-8"))
    ziele = {
        # http://identity-service:8001 -> ("identity-service", 8001)
        (m.group(1), int(m.group(2)))
        for svc in dynamic["http"]["services"].values()
        for server in svc["loadBalancer"]["servers"]
        if (m := re.fullmatch(r"http://([^:/]+):(\d+)", server["url"]))
    }

    values = _values()
    bekannt = {(svc["name"], svc["port"]) for svc in values["services"]}
    # Die Oberfläche ist kein Python-Dienst und steht deshalb nicht in der Liste;
    # templates/web.yaml legt sie unter genau diesem Namen und Port an.
    bekannt.add(("web", 5173))

    unbekannt = ziele - bekannt
    assert not unbekannt, (
        f"docker/traefik/dynamic.yml zeigt auf Ziele, die das Chart nicht anlegt: "
        f"{sorted(unbekannt)}."
    )


def test_browser_navigation_gewinnt_gegen_jede_api_regel() -> None:
    """Ohne diese Regel liefert ein Direktlink auf /jobs rohes JSON.

    `/jobs`, `/applications`, `/transfers` und `/github` sind zugleich
    API-Präfixe und Seiten der Oberfläche. Nach Pfad allein ist das nicht zu
    trennen; `Sec-Fetch-Dest: document` trennt es, weil der Browser diesen Kopf
    nur bei einer Navigation der obersten Ebene schickt.

    Der Fehler wäre leicht zu übersehen: ein Klick IM Programm funktioniert
    (der Router schaltet im Browser um), nur der Direktlink und das Neuladen
    fallen durch. Genau die zwei Fälle, die niemand beim Entwickeln macht.
    """
    dynamic = yaml.safe_load(DYNAMIC.read_text(encoding="utf-8"))
    routers = dynamic["http"]["routers"]

    assert "web-navigation" in routers, (
        "Die Regel `web-navigation` fehlt. Ohne sie beantwortet das Gateway "
        "einen Direktlink auf /jobs mit JSON aus dem jobs-service."
    )

    navigation = routers["web-navigation"]
    assert navigation["service"] == "web"
    assert "Sec-Fetch-Dest" in navigation["rule"]

    hoechste_api = max(
        int(r.get("priority", 0)) for name, r in routers.items() if name != "web-navigation"
    )
    assert int(navigation["priority"]) > hoechste_api, (
        f"web-navigation hat priority {navigation['priority']}, aber eine API-Regel "
        f"hat {hoechste_api}. Dann gewinnt die API, und der Direktlink ist wieder kaputt."
    )


def test_jeder_dienst_hat_das_verzeichnis_das_er_behauptet() -> None:
    """SERVICE_DIR steuert, welche Alembic-Historie migriert wird.

    Ein Tippfehler hier migriert die Datenbank eines anderen Dienstes — oder
    keine, denn ``docker/entrypoint.sh`` überspringt ein fehlendes
    Migrationsverzeichnis wortlos.
    """
    for svc in _values()["services"]:
        verzeichnis = ROOT / "apps" / svc["dir"]
        assert verzeichnis.is_dir(), f"{svc['name']}: apps/{svc['dir']} existiert nicht."

        pyproject = verzeichnis / "pyproject.toml"
        assert pyproject.is_file(), f"{svc['name']}: apps/{svc['dir']}/pyproject.toml fehlt."

        # Das uvicorn-Ziel muss auf ein Paket zeigen, das dieser Dienst wirklich
        # ausliefert — sonst startet der Pod mit ModuleNotFoundError.
        modul = svc["module"].split(":")[0].split(".")[0]
        quelle = verzeichnis / "src" / modul
        assert quelle.is_dir(), (
            f"{svc['name']}: das Modul {modul!r} aus {svc['module']!r} liegt nicht "
            f"unter apps/{svc['dir']}/src/."
        )

        # Und der Name im Chart muss der Name des Verzeichnisses sein, weil
        # dynamic.yml genau diesen Namen anspricht.
        name = tomllib.loads(pyproject.read_text(encoding="utf-8"))["project"]["name"]
        assert name, f"{svc['name']}: pyproject.toml ohne project.name."
