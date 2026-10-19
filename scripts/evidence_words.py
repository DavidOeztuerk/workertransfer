#!/usr/bin/env python3
"""Searches a generated document for words that claim too much.

EVIDENCE, NOT COMPLIANCE. Under Art. 42/43 GDPR only a supervisory authority or
a body accredited to EN ISO/IEC 17065 may certify; "certified" without
accreditation is a misleading commercial practice in the EU (Directive
2005/29/EC, Directive 2006/114/EC). That is a real risk, not a theoretical one.

CHECKED ARE THE SCOPE SENTENCE AND THE CAVEATS, NOT THE DISCLAIMER ITSELF — and
that was measured on the first run, not thought of beforehand. The fixed
disclaimer says literally "Es ist KEIN Zertifikat und keine Bescheinigung", and
a word search that reads it reports exactly the sentence that DENIES the claim.
Telling a denial from a claim by machine would only work with a pattern that is
wrong at the next half-sentence.

It stays checked, only elsewhere: `DocumentWordTests` pins its wording, and
that test runs without a stack. What is checked here is what CHANGES — the
scope sentence per document and the caveats derived from the reading, which
therefore stand in no constant.
"""

import json
import sys

# The German words stay: the documents are German. The English ones stand
# beside them, the same list `DocumentWordTests.Forbidden` holds — a finding
# summary from Noelia arrives in English, and a list that only knows German
# would let an English "compliant" through.
FORBIDDEN = [
    "zertifi", "konform", "compliant", "certif", "bescheinig",
    "attest", "auditiert", "erfuellt art", "erfüllt art",
    "conform", "compliance", "complies", "audited", "fulfils art", "fulfills art",
]


def main() -> int:
    with open(sys.argv[1], encoding="utf-8") as handle:
        document = json.load(handle)

    fields = [("scope", document["scope"])]
    fields += [
        (f"caveats[{position}]", sentence)
        for position, sentence in enumerate(document["caveats"])
    ]

    for name, text in fields:
        for word in FORBIDDEN:
            if word in text.lower():
                print(f"{name}: „{word}“")

    return 0


if __name__ == "__main__":
    sys.exit(main())
