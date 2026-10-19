#!/usr/bin/env python3
"""Builds `PostalCodes.txt` from the GeoNames raw data.

Source: https://download.geonames.org/export/zip/{DE,AT,CH}.zip
Licence: CC BY 4.0 — the attribution stands in `NOTICE.md` and in the imprint.

Call:
    python3 scripts/postal-codes.py <directory-with-DE.txt-AT.txt-CH.txt>

The result is text ON PURPOSE and not packed: it lives in version control, and
whoever changes a line should see in the diff which one.

The section markers `[plz]` and `[orte]` in the output are a format contract
with `PlaceLookup` (src/shared/WorkerTransfer.ServiceDefaults), which reads
them by name; they stay as they are.
"""
import math
import pathlib
import sys

# How far apart the rows of ONE postal code may lie.
#
# The reason is the German bulk-recipient postal code: `10875` has thirty-six
# rows, all with company names instead of place names, with coordinates from
# Stuttgart via Berlin to Bautzen. A postal code whose rows differ by hundreds
# of kilometres denotes no place but a recipient — it is dropped entirely
# instead of inventing an average that lies somewhere in the middle of nowhere.
SPREAD_KM = 30.0

# How clearly a place must tower over its namesakes for its name to stand for
# it.
#
# "Husum" exists five times in Germany: once with 20841 inhabitants in North
# Frisia and four times as a village. Dropping the name for that would hit
# exactly the place everyone means. "Neustadt", on the other hand, is truly
# ambiguous, and there "don't know" is the right answer — it is counted and
# named (ADR-0032); a miss by four hundred kilometres is not.
DOMINANCE = 3.0

# From which size a place name enters the name register at all.
#
# Without this threshold the file holds a hundred and nine thousand names —
# every hamlet and every spelling variant, 2.7 MB nobody ever looks up.
# Whoever names a place under a thousand inhabitants in an advert is found via
# the POSTAL CODE; that is what it is for. Names that denote a postal code stay
# regardless of size.
MIN_INHABITANTS = 1000

# From which size name forms in other languages are taken along.
#
# The UI speaks German, English and French (ADR-0031), and an English advert
# says "Munich" or "Cologne". Only for large cities, because a village's name
# list in the data set holds mostly spelling variants and nonsense.
FOREIGN_NAMES_FROM = 20000


def distance_km(a, b):
    (lat1, lon1), (lat2, lon2) = a, b
    d_lat = math.radians(lat2 - lat1)
    d_lon = math.radians(lon2 - lon1)
    h = (math.sin(d_lat / 2) ** 2
         + math.cos(math.radians(lat1)) * math.cos(math.radians(lat2))
         * math.sin(d_lon / 2) ** 2)
    return 6371.0 * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h))


def centre(points):
    return (sum(p[0] for p in points) / len(points),
            sum(p[1] for p in points) / len(points))


def read_postal_codes(path):
    """(country, postal code, place, latitude, longitude) per row of the postal code file."""
    for row in path.read_text(encoding="utf-8").splitlines():
        parts = row.split("\t")
        if len(parts) < 11 or parts[9] == "" or parts[10] == "":
            continue
        try:
            yield parts[0], parts[1], parts[2], float(parts[9]), float(parts[10])
        except ValueError:
            continue


def read_places(path):
    """(name, latitude, longitude, inhabitants) per populated place of the full dump."""
    for row in path.read_text(encoding="utf-8").splitlines():
        parts = row.split("\t")
        # Feature class P means "populated place". Everything else is
        # mountains, rivers, stations and administrative areas — none of that
        # stands as a place of work in an advert.
        if len(parts) < 15 or parts[6] != "P":
            continue
        try:
            lat, lon, inhabitants = float(parts[4]), float(parts[5]), int(parts[14])
        except ValueError:
            continue

        names = {parts[1], parts[2]}
        if inhabitants >= FOREIGN_NAMES_FROM and parts[3] != "":
            names.update(parts[3].split(","))

        for name in names:
            clean = name.strip()
            # No characters a place name does not have: brackets, digits,
            # slashes. They come from spelling variants and abbreviations.
            if clean == "" or len(clean) < 2 or len(clean) > 60:
                continue
            if any(character.isdigit() for character in clean):
                continue
            if any(character in clean for character in "()[]/\\|,;:"):
                continue
            yield clean, lat, lon, inhabitants


def group(points):
    """Points closer together than SPREAD_KM, in one group."""
    groups = []
    for point in points:
        for existing in groups:
            if distance_km(centre([(p[0], p[1]) for p in existing]),
                           (point[0], point[1])) <= SPREAD_KM:
                existing.append(point)
                break
        else:
            groups.append([point])
    return groups


def main():
    source = pathlib.Path(sys.argv[1])
    target = pathlib.Path(__file__).parent.parent / (
        "src/shared/WorkerTransfer.ServiceDefaults/PostalCodes.txt")

    # --- Place names first -------------------------------------------------
    #
    # They are NEEDED before the postal codes come about: only what also
    # stands in the place register may name a postal code. See below.
    by_name = {}
    is_place = set()

    for country in ("DE", "AT", "CH"):
        for name, lat, lon, inhabitants in read_places(
                source / f"dump-{country}" / f"{country}.txt"):
            by_name.setdefault(name, []).append((lat, lon, inhabitants))
            is_place.add(name.lower())

    # --- Postal codes ------------------------------------------------------
    by_postal_code = {}
    for country in ("DE", "AT", "CH"):
        for row in read_postal_codes(source / country / f"{country}.txt"):
            by_postal_code.setdefault((row[0], row[1]), []).append(row)

    postal_code_rows = []
    dropped = 0

    for (country, postal_code), rows in sorted(by_postal_code.items()):
        points = [(r[3], r[4]) for r in rows]
        centroid = centre(points)

        # The bulk-recipient probe. See SPREAD_KM.
        if max(distance_km(centroid, p) for p in points) > SPREAD_KM:
            dropped += 1
            continue

        # The most frequent place name of this postal code stands beside it —
        # it resolves an ambiguous four-digit code (AT and CH share the range
        # 1000–9999) and answers the reverse direction "which place lies
        # here?".
        #
        # IT MUST BE A PLACE. Measured: the German data list companies and
        # authorities as "place names" under bulk-recipient postal codes, and
        # while that got through, the reverse direction answered "Adam Opel
        # GmbH" for Berlin and "Amtsgericht München" for Munich. The spread
        # probe above does not catch that: if all rows lie in ONE city, nothing
        # spreads. So it is checked against the place register — "Berlin"
        # stands there, "Adam Opel GmbH" does not.
        frequency = {}
        for r in rows:
            # The part before the comma counts too: Austria lists districts as
            # "Wien, Innere Stadt", and that stands in no place register —
            # "Wien" does. Without this line postal code 1010 dropped out as an
            # alleged bulk recipient.
            name = r[2].split(",")[0].strip()

            if r[2].lower() in is_place or name.lower() in is_place:
                frequency[name] = frequency.get(name, 0) + 1

        # Not a single real place name: then the code is no place but a
        # recipient, and it does not belong in the table at all.
        if not frequency:
            dropped += 1
            continue

        place = max(frequency, key=lambda name: (frequency[name], -len(name)))

        postal_code_rows.append(
            f"{country}\t{postal_code}\t{centroid[0]:.3f}\t{centroid[1]:.3f}\t{place}")

    # --- Place names -------------------------------------------------------
    # Every name that denotes a postal code stays — even a tiny one.
    postal_code_names = set()
    for row in postal_code_rows:
        place = row.split("\t")[4].lower()
        postal_code_names.add(place)
        postal_code_names.add(place.split(",")[0].strip())

    name_rows = []
    ambiguous = 0
    too_small = 0

    for name, occurrences in sorted(by_name.items()):
        groups = group(occurrences)
        groups.sort(key=lambda g: sum(p[2] for p in g), reverse=True)

        # One alone always wins. Otherwise it has to tower clearly over the
        # others — see DOMINANCE.
        if len(groups) > 1:
            first = sum(p[2] for p in groups[0])
            second = sum(p[2] for p in groups[1])
            if first == 0 or first < second * DOMINANCE:
                ambiguous += 1
                continue

        if (sum(p[2] for p in groups[0]) < MIN_INHABITANTS
                and name.lower() not in postal_code_names):
            too_small += 1
            continue

        centroid = centre([(p[0], p[1]) for p in groups[0]])
        name_rows.append(
            f"{name}\t{centroid[0]:.3f}\t{centroid[1]:.3f}")

    target.write_text(
        "# Postal codes and places in DE, AT and CH.\n"
        "# GENERATED by scripts/postal-codes.py — do not edit by hand.\n"
        "# Source: GeoNames (download.geonames.org), CC BY 4.0. See NOTICE.md.\n"
        "#\n"
        "# Section [plz]:  country, postal code, latitude, longitude, place\n"
        "# Section [orte]: place, latitude, longitude\n"
        "[plz]\n" + "\n".join(postal_code_rows) + "\n"
        "[orte]\n" + "\n".join(name_rows) + "\n",
        encoding="utf-8")

    print(f"{len(postal_code_rows)} postal codes, {len(name_rows)} places; dropped "
          f"{dropped} bulk-recipient codes, {ambiguous} ambiguous and "
          f"{too_small} too small names; "
          f"{target.stat().st_size // 1024} kB")


if __name__ == "__main__":
    main()
