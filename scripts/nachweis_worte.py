#!/usr/bin/env python3
"""Sucht in einem erzeugten Dokument nach Worten, die zu viel behaupten.

BELEGE, KEINE KONFORMITAET. Nach Art. 42/43 DSGVO darf nur eine
Aufsichtsbehoerde oder eine nach EN ISO/IEC 17065 akkreditierte Stelle
zertifizieren; „zertifiziert" ohne Akkreditierung ist in der EU eine
irrefuehrende Geschaeftspraxis (RL 2005/29/EG, RL 2006/114/EG). Das ist ein
echtes Risiko, kein theoretisches.

GEPRUEFT WERDEN GELTUNGSSATZ UND VORBEHALTE, NICHT DER VORBEHALTSSATZ SELBST —
und das ist beim ersten Lauf gemessen worden, nicht vorher bedacht. Der feste
Vorbehalt sagt woertlich „Es ist KEIN Zertifikat und keine Bescheinigung", und
eine Wortsuche, die ihn mitliest, meldet genau den Satz, der die Behauptung
VERNEINT. Eine Verneinung maschinell von einer Behauptung zu unterscheiden,
ginge nur mit einem Muster, das beim naechsten Halbsatz falsch liegt.

Er bleibt trotzdem geprueft, nur woanders: `DokumentwortTests` nagelt seinen
Wortlaut fest, und dieser Test laeuft ohne Stapel. Was hier geprueft wird, ist
das, was sich AENDERT — der Geltungssatz je Dokument und die Vorbehalte, die
aus der Lesung abgeleitet werden und deshalb in keiner Konstante stehen.
"""

import json
import sys

UNTERSAGT = [
    "zertifi", "konform", "compliant", "certif", "bescheinig",
    "attest", "auditiert", "erfuellt art", "erfüllt art",
]


def main() -> int:
    with open(sys.argv[1], encoding="utf-8") as offen:
        dokument = json.load(offen)

    felder = [("scope", dokument["scope"])]
    felder += [
        (f"caveats[{stelle}]", satz)
        for stelle, satz in enumerate(dokument["caveats"])
    ]

    for name, text in felder:
        for wort in UNTERSAGT:
            if wort in text.lower():
                print(f"{name}: „{wort}“")

    return 0


if __name__ == "__main__":
    sys.exit(main())
