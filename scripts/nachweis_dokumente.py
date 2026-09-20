#!/usr/bin/env python3
"""Aus den eingesammelten Berichten werden drei Dokumente.

DREI UND NICHT EINES, weil sie von drei Menschen fuer drei Zwecke gelesen
werden — Datenschutzbeauftragter, wer die Anhang-III-Frage beantwortet,
Betriebsrat — und weil man aus einem gemischten Dokument den einen Teil
zitieren kann, ohne den Teil zu zitieren, der ihn einschraenkt.

DER GELTUNGSSATZ STEHT INNERHALB DER SIGNATUR. Ein Umfang, der ausserhalb
steht, ist ein Umfang, den jemand umformulieren kann.

DIE VORBEHALTE WERDEN ABGELEITET, nicht angehaengt. Eine feste Liste liest man
einmal; eine, die sich mit der Lage aendert, liest man jedes Mal.
"""

import json
import pathlib
import sys
from datetime import datetime, timezone

# ---------------------------------------------------------------------------
# DER SATZ, DER IN JEDEM DOKUMENT INNERHALB DER SIGNATUR STEHT.
#
# Er sagt drei Dinge, und jedes davon fehlt in den Broschueren, gegen die
# dieses Dokument antritt: dass es maschinell erzeugt ist, dass es kein
# Zertifikat ist, und dass niemand mit einer Akkreditierung es angesehen hat.
# ---------------------------------------------------------------------------
VORBEHALT = (
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

DOKUMENTE = {
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
        # Nach KENNUNGSPRAEFIX statt nach eigenem Bereich: seit ADR-0045
        # kommen die Befunde aus Noelias Laeufer und tragen keine `area` mehr.
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


def lade(verzeichnis: pathlib.Path) -> list[dict]:
    """Die Berichte, nach Dienstnamen sortiert."""
    berichte = []
    for datei in sorted(verzeichnis.glob("*.json")):
        with datei.open(encoding="utf-8") as offen:
            berichte.append(json.load(offen))
    return sorted(berichte, key=lambda bericht: bericht["service"])


def passend(bericht: dict, muster: dict) -> list[dict]:
    """Die Befunde eines Berichts, die in dieses Dokument gehoeren."""
    return [
        befund
        for befund in bericht["securityChecks"]["results"]
        if befund["id"] in muster["ids"]
        or (muster["prefixes"] and befund["id"].startswith(tuple(muster["prefixes"])))
    ]


def vorbehalte(dienste: list[dict], stumm: list[str]) -> list[str]:
    """Was dieses Dokument offenlaesst — aus der Lesung, nicht als Baustein.

    Eine feste Liste liest man einmal. Diese hier aendert sich mit dem Stand,
    und wer sie zweimal liest, sieht den Unterschied.
    """
    satz = []

    # Der wichtigste zuerst: wer nicht geantwortet hat, steht NAMENTLICH da.
    # Ein Dienst, der schweigt, darf nicht wie einer aussehen, der nichts zu
    # melden hat.
    if stumm:
        satz.append(
            "Nicht gelesen werden konnten: "
            + ", ".join(sorted(stumm))
            + ". Ueber diese Dienste sagt dieses Dokument NICHTS — weder "
            "Gutes noch Schlechtes."
        )

    alle = [
        (dienst["service"], befund)
        for dienst in dienste
        for befund in dienst["findings"]
    ]

    fehlt = [(name, befund) for name, befund in alle if befund["status"] == "Fail"]
    hinweis = [(name, befund) for name, befund in alle if befund["status"] == "Warning"]

    for name, befund in fehlt:
        satz.append(
            f"NICHT EINGELOEST — {name}, {befund['id']}: {befund['summary']} "
            f"{befund['remediation']}"
        )

    for name, befund in hinweis:
        satz.append(f"Offen — {name}, {befund['id']}: {befund['summary']}")

    nicht = [
        (name, befund)
        for name, befund in alle
        if befund["status"] == "NotApplicable"
    ]
    if nicht:
        satz.append(
            "Ohne Gegenstand in dieser Instanz und deshalb ohne Aussage: "
            + ", ".join(sorted({f"{name}/{befund['id']}" for name, befund in nicht}))
            + "."
        )

    # Die Grenze dieses Werkzeugs, immer, und nicht als Floskel: sie ist der
    # Grund, warum die Personen-Zeile im KI-Verzeichnis eine ZAHL ist.
    satz.append(
        "Dieses Dokument nennt keinen Menschen und keinen Wert. Wo es zaehlt, "
        "zaehlt es ueber Menschen — welcher Mensch welchen Anbieter benutzt, "
        "steht hier nicht und entsteht auch nicht."
    )

    satz.append(
        "Ein Verzeichnis sagt, was zum Zeitpunkt der Lesung eingetragen war. "
        "Die Person waehlt ihren KI-Anbieter selbst; was morgen eingetragen "
        "wird, kann dieses Dokument nicht sagen."
    )

    return satz


def zitate(dienste: list[dict]) -> list[dict]:
    """Die Zitate dieses Dokuments, einmal je Artikel."""
    gesehen: dict[str, dict] = {}
    for dienst in dienste:
        for befund in dienst["findings"]:
            for bezug in befund["references"]:
                schluessel = bezug["citation"]
                gesehen.setdefault(schluessel, bezug)
    return [gesehen[schluessel] for schluessel in sorted(gesehen)]


def main() -> int:
    quelle = pathlib.Path(sys.argv[1])
    ziel = pathlib.Path(sys.argv[2])
    stumm = [name for name in sys.argv[3].split() if name] if len(sys.argv) > 3 else []

    berichte = lade(quelle)
    if not berichte:
        print("Keine Berichte eingesammelt.", file=sys.stderr)
        return 1

    jetzt = datetime.now(timezone.utc).replace(microsecond=0).isoformat()
    ziel.mkdir(parents=True, exist_ok=True)

    for name, muster in DOKUMENTE.items():
        dienste = []
        for bericht in berichte:
            befunde = passend(bericht, muster)
            if befunde:
                dienste.append(
                    {
                        "service": bericht["service"],
                        "read_at": bericht["generatedAt"],
                        "findings": befunde,
                    }
                )

        dokument = {
            "document": name,
            "title": muster["title"],
            "reader": muster["reader"],
            # DER GELTUNGSSATZ STEHT INNERHALB DER SIGNATUR.
            "scope": muster["scope"],
            "disclaimer": VORBEHALT,
            "generated_at": jetzt,
            "services_read": len(berichte),
            "services_silent": sorted(stumm),
            "services": dienste,
            "caveats": vorbehalte(dienste, stumm),
            "references": zitate(dienste),
        }

        with (ziel / f"{name}.json").open("w", encoding="utf-8") as offen:
            json.dump(dokument, offen, indent=2, ensure_ascii=False, sort_keys=True)
            offen.write("\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())
