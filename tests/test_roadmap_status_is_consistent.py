"""Die Statustabelle und die Abschnittsüberschriften müssen dasselbe sagen.

Der Anlass ist eine Rückfrage: *„wieso ist schritt 5 noch nicht als erledigt
markiert bzw. hat noch den orangenen flag?"* — die Tabelle sagte ✅, die
Überschrift 🟧. Beim Nachsehen stand Phase 6 genauso da.

Das ist kein Schönheitsfehler. `docs/ROADMAP.md` ist der Statusindex, den man
liest, um zu entscheiden, woran als Nächstes gearbeitet wird. Sagen die beiden
Stellen verschiedenes, ist die Datei genau dort unbrauchbar, wo sie gebraucht
wird — und man merkt es nur, wenn man beide Stellen zufällig nebeneinander
hält. Der Fehler entsteht ganz beiläufig: man hakt eine Phase in der Tabelle ab
und scrollt nicht 400 Zeilen weiter zur Überschrift.
"""

from __future__ import annotations

import re
from pathlib import Path

_ROADMAP = Path(__file__).resolve().parents[1] / "docs" / "ROADMAP.md"

#: `| 5 | Transfermarkt | ✅ | … |`
_ROW = re.compile(r"^\|\s*(\d+(?:\.\d+)?)\s*\|[^|]*\|\s*([⬜🟧✅⛔])\s*\|")
#: `### Phase 5 — Status: ✅ erledigt (…)`
_HEADING = re.compile(r"^###\s+Phase\s+(\d+(?:\.\d+)?)\s+—\s+Status:\s*([⬜🟧✅⛔])")


def _statuses() -> tuple[dict[str, str], dict[str, str]]:
    table: dict[str, str] = {}
    headings: dict[str, str] = {}
    for line in _ROADMAP.read_text(encoding="utf-8").splitlines():
        row = _ROW.match(line)
        if row:
            table[row.group(1)] = row.group(2)
            continue
        heading = _HEADING.match(line)
        if heading:
            headings[heading.group(1)] = heading.group(2)
    return table, headings


def test_the_roadmap_parses_at_all() -> None:
    """Sonst wäre der Test darunter grün, weil er nichts gefunden hat.

    Ein Wächter, der bei einer Formatänderung still zu null Vergleichen
    schrumpft, meldet „alles in Ordnung" über einer Datei, die er nicht mehr
    liest — dieselbe Klasse Fehler wie ein E2E-Lauf, der sich vollständig
    überspringt und trotzdem grün berichtet.
    """
    table, headings = _statuses()

    assert len(table) >= 10, f"Statustabelle nicht gefunden oder zu kurz: {sorted(table)}"
    assert len(headings) >= 5, f"Phasen-Überschriften nicht gefunden: {sorted(headings)}"


def test_every_phase_says_the_same_in_both_places() -> None:
    table, headings = _statuses()

    disagreeing = {
        phase: (table[phase], status)
        for phase, status in headings.items()
        if phase in table and table[phase] != status
    }

    assert not disagreeing, (
        "ROADMAP.md widerspricht sich — Tabelle vs. Abschnittsüberschrift: "
        + ", ".join(
            f"Phase {phase}: Tabelle {row}, Überschrift {head}"
            for phase, (row, head) in sorted(disagreeing.items())
        )
    )
