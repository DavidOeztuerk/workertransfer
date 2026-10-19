#!/usr/bin/env python3
"""Turns the collected reports into three documents.

THREE AND NOT ONE, because three people read them for three purposes — the
data protection officer, whoever answers the Annex III question, the works
council — and because from a mixed document one can quote the one part without
quoting the part that limits it.

THE SCOPE SENTENCE STANDS INSIDE THE SIGNATURE. A scope that stands outside is
a scope somebody can reword.

THE CAVEATS ARE DERIVED, not appended. A fixed list gets read once; one that
changes with the state gets read every time.

The TEXT of the documents stays German: they are written for German readers
(data protection officer, works council). The check ids (`wt.loeschung.`, …)
are the ids the services report and stay as the services spell them.
"""

import json
import pathlib
import sys
from datetime import datetime, timezone

# ---------------------------------------------------------------------------
# THE SENTENCE THAT STANDS INSIDE THE SIGNATURE IN EVERY DOCUMENT.
#
# It says three things, and each of them is missing from the brochures this
# document stands against: that it is machine-generated, that it is no
# certificate, and that nobody with an accreditation has looked at it.
# `DocumentWordTests` reads this constant by its name.
# ---------------------------------------------------------------------------
DISCLAIMER = (
    "Dies ist maschinell erzeugte technische Evidenz ueber den Stand der "
    "genannten Dienste zum genannten Zeitpunkt. Es ist KEIN Zertifikat und "
    "keine Bescheinigung: nach Art. 42/43 DSGVO darf nur eine Aufsichtsbehoerde "
    "oder eine nach EN ISO/IEC 17065 akkreditierte Stelle zertifizieren, und "
    "keine solche Stelle hat dieses Dokument oder das darin beschriebene System "
    "beurteilt. Ein Programm kann zeigen, dass etwas vorhanden ist, wann es "
    "entstanden ist und dass es unveraendert ist. Ob es GENUEGT, kann es nicht "
    "zeigen — das entscheidet ein Mensch, und jede Zeile dieses Dokuments sagt "
    "in ihrer letzten Spalte, was dabei offenbleibt."
)

DOCUMENTS = {
    "datenschutz": {
        "title": "Nachweis Datenschutz",
        "reader": "Datenschutzbeauftragte oder Datenschutzbeauftragter",
        "scope": (
            "Welche Empfaenger die Dienste dieser Instanz ansprechen, wo diese "
            "Empfaenger nach ihrem Namen liegen, ob ein Widerruf beim naechsten "
            "Zugriff wirkt und ob eine Loeschung jeden Dienst erreichen kann. "
            "NICHT enthalten: was tatsaechlich gesendet wurde, ob "
            "personenbezogene Daten im Inhalt stehen, ob ein "
            "Auftragsverarbeitungsvertrag traegt und ob eine "
            "Uebermittlungsgarantie greift."
        ),
        # By ID PREFIX instead of by an area of our own: since ADR-0045 the
        # findings come from Noelia's runner and no longer carry an `area`.
        "prefixes": [
            "noelia.egress.", "noelia.destination.", "noelia.audit.",
            "noelia.dataprotection.", "wt.loeschung.", "wt.einwilligung.",
            "wt.ledger.",
        ],
        "ids": ["wt.ki.anbieter", "noelia.ai.transfer"],
    },
    "ki": {
        "title": "Nachweis KI-Einsatz",
        "reader": "Wer die Einstufung nach Anhang III der KI-VO beantwortet",
        "scope": (
            "Welche Modelle und Anbieter in dieser Instanz eingetragen sind, "
            "welche Felder den Dienst in Richtung eines Modells verlassen, ob "
            "Modellaufrufe aufgezeichnet werden und ob irgendeine oeffentliche "
            "Flaeche eine Zahl ueber einen Menschen traegt. NICHT enthalten: "
            "eine Einstufung nach Anhang III. Dieses Dokument stellt die Frage "
            "und legt die Belege daneben; beantwortet wird sie von einem "
            "Menschen mit juristischer Ausbildung."
        ),
        "prefixes": ["wt.ki.", "noelia.ai."],
        "ids": [],
    },
    "mitbestimmung": {
        "title": "Nachweis Mitbestimmung",
        "reader": "Betriebsrat",
        "scope": (
            "Was dieses System ueber Beschaeftigte erfassen kann und was "
            "nachweislich nicht: ob eine Zahl ueber einen Menschen entsteht, "
            "welche Angaben ein Modell erreichen, ob Aufrufe aufgezeichnet "
            "werden und ob eine Freigabe sofort zurueckgenommen werden kann. "
            "NICHT enthalten: ob eine Betriebsvereinbarung noetig ist und was "
            "in ihr stehen muss — das entscheiden die Betriebsparteien."
        ),
        "prefixes": [],
        "ids": [
            "wt.ki.keine-zahl",
            "wt.ki.naht",
            "wt.ki.anbieter",
            "wt.einwilligung.wirkt",
            "noelia.ai.record-keeping",
        ],
    },
}


def load(directory: pathlib.Path) -> list[dict]:
    """The reports, sorted by service name."""
    reports = []
    for path in sorted(directory.glob("*.json")):
        with path.open(encoding="utf-8") as handle:
            reports.append(json.load(handle))
    return sorted(reports, key=lambda report: report["service"])


def matching(report: dict, pattern: dict) -> list[dict]:
    """The findings of a report that belong in this document."""
    return [
        finding
        for finding in report["securityChecks"]["results"]
        if finding["id"] in pattern["ids"]
        or (pattern["prefixes"] and finding["id"].startswith(tuple(pattern["prefixes"])))
    ]


def caveats(services: list[dict], silent: list[str]) -> list[str]:
    """What this document leaves open — from the reading, not as a building block.

    A fixed list gets read once. This one changes with the state, and whoever
    reads it twice sees the difference.
    """
    sentences = []

    # The most important one first: whoever did not answer stands there BY
    # NAME. A service that stays silent must not look like one that has
    # nothing to report.
    if silent:
        sentences.append(
            "Nicht gelesen werden konnten: "
            + ", ".join(sorted(silent))
            + ". Ueber diese Dienste sagt dieses Dokument NICHTS — weder "
            "Gutes noch Schlechtes."
        )

    everything = [
        (service["service"], finding)
        for service in services
        for finding in service["findings"]
    ]

    failed = [(name, finding) for name, finding in everything if finding["status"] == "Fail"]
    warned = [(name, finding) for name, finding in everything if finding["status"] == "Warning"]

    for name, finding in failed:
        sentences.append(
            f"NICHT EINGELOEST — {name}, {finding['id']}: {finding['summary']} "
            f"{finding['remediation']}"
        )

    for name, finding in warned:
        sentences.append(f"Offen — {name}, {finding['id']}: {finding['summary']}")

    not_applicable = [
        (name, finding)
        for name, finding in everything
        if finding["status"] == "NotApplicable"
    ]
    if not_applicable:
        sentences.append(
            "Ohne Gegenstand in dieser Instanz und deshalb ohne Aussage: "
            + ", ".join(sorted({f"{name}/{finding['id']}" for name, finding in not_applicable}))
            + "."
        )

    # The limit of this tool, always, and not as a phrase: it is the reason
    # why the people row in the AI directory is a NUMBER.
    sentences.append(
        "Dieses Dokument nennt keinen Menschen und keinen Wert. Wo es zaehlt, "
        "zaehlt es ueber Menschen — welcher Mensch welchen Anbieter benutzt, "
        "steht hier nicht und entsteht auch nicht."
    )

    sentences.append(
        "Ein Verzeichnis sagt, was zum Zeitpunkt der Lesung eingetragen war. "
        "Die Person waehlt ihren KI-Anbieter selbst; was morgen eingetragen "
        "wird, kann dieses Dokument nicht sagen."
    )

    return sentences


def citations(services: list[dict]) -> list[dict]:
    """The citations of this document, once per article."""
    seen: dict[str, dict] = {}
    for service in services:
        for finding in service["findings"]:
            for reference in finding["references"]:
                key = reference["citation"]
                seen.setdefault(key, reference)
    return [seen[key] for key in sorted(seen)]


def main() -> int:
    source = pathlib.Path(sys.argv[1])
    target = pathlib.Path(sys.argv[2])
    silent = [name for name in sys.argv[3].split() if name] if len(sys.argv) > 3 else []

    reports = load(source)
    if not reports:
        print("No reports collected.", file=sys.stderr)
        return 1

    now = datetime.now(timezone.utc).replace(microsecond=0).isoformat()
    target.mkdir(parents=True, exist_ok=True)

    for name, pattern in DOCUMENTS.items():
        services = []
        for report in reports:
            findings = matching(report, pattern)
            if findings:
                services.append(
                    {
                        "service": report["service"],
                        "read_at": report["generatedAt"],
                        "findings": findings,
                    }
                )

        document = {
            "document": name,
            "title": pattern["title"],
            "reader": pattern["reader"],
            # THE SCOPE SENTENCE STANDS INSIDE THE SIGNATURE.
            "scope": pattern["scope"],
            "disclaimer": DISCLAIMER,
            "generated_at": now,
            "services_read": len(reports),
            "services_silent": sorted(silent),
            "services": services,
            "caveats": caveats(services, silent),
            "references": citations(services),
        }

        with (target / f"{name}.json").open("w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2, ensure_ascii=False, sort_keys=True)
            handle.write("\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
