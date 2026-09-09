#!/usr/bin/env python3
"""Baut `Postleitzahlen.txt` aus den GeoNames-Rohdaten.

Quelle: https://download.geonames.org/export/zip/{DE,AT,CH}.zip
Lizenz: CC BY 4.0 — die Namensnennung steht in `NOTICE.md` und im Impressum.

Aufruf:
    python3 scripts/plz-tabelle.py <verzeichnis-mit-DE.txt-AT.txt-CH.txt>

Das Ergebnis ist ABSICHTLICH Text und nicht gepackt: es liegt in der Versions-
verwaltung, und wer eine Zeile ändert, soll im Vergleich sehen, welche.
"""
import math
import pathlib
import sys

# Wie weit die Zeilen EINER Postleitzahl auseinanderliegen dürfen.
#
# Der Grund ist die deutsche Großkunden-PLZ: `10875` hat sechsunddreißig
# Zeilen, alle mit Firmennamen statt Ortsnamen, mit Koordinaten von Stuttgart
# über Berlin bis Bautzen. Eine Postleitzahl, deren Zeilen sich um Hunderte
# Kilometer unterscheiden, bezeichnet keinen Ort, sondern einen Empfänger —
# sie fliegt ganz raus, statt einen Mittelwert zu erfinden, der irgendwo im
# Nirgendwo liegt.
STREUUNG_KM = 30.0

# Wie stark ein Ort seine Namensvettern überragen muss, damit sein Name für
# ihn steht.
#
# „Husum" gibt es fünfmal in Deutschland: einmal mit 20841 Einwohnern in
# Nordfriesland und viermal als Dorf. Den Namen deswegen ganz zu verwerfen
# träfe ausgerechnet den Ort, den jeder meint. „Neustadt" dagegen ist wirklich
# mehrdeutig, und dort ist „weiss ich nicht" die richtige Antwort — sie wird
# gezählt und genannt (ADR-0032), ein Fehlgriff um vierhundert Kilometer nicht.
UEBERLEGENHEIT = 3.0

# Ab welcher Grösse ein Ortsname überhaupt ins Namensregister kommt.
#
# Ohne diese Schwelle stehen hundertneuntausend Namen in der Datei — jeder
# Weiler und jede Schreibvariante, 2,7 MB, die niemand je nachschlägt. Wer in
# einer Anzeige einen Ort unter tausend Einwohnern nennt, wird über die
# POSTLEITZAHL gefunden; dafür ist sie da. Namen, die eine Postleitzahl
# bezeichnen, bleiben unabhängig von der Grösse drin.
MINDEST_EINWOHNER = 1000

# Ab welcher Grösse fremdsprachige Namensformen mitgenommen werden.
#
# Die Oberfläche spricht Deutsch, Englisch und Französisch (ADR-0031), und in
# einer englischen Anzeige steht „Munich" oder „Cologne". Nur für grosse Städte,
# weil die Namensliste eines Dorfes im Datensatz vor allem Schreibvarianten und
# Unsinn enthält.
FREMDNAMEN_AB = 20000


def entfernung_km(a, b):
    (breite1, laenge1), (breite2, laenge2) = a, b
    d_breite = math.radians(breite2 - breite1)
    d_laenge = math.radians(laenge2 - laenge1)
    h = (math.sin(d_breite / 2) ** 2
         + math.cos(math.radians(breite1)) * math.cos(math.radians(breite2))
         * math.sin(d_laenge / 2) ** 2)
    return 6371.0 * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h))


def mitte(punkte):
    return (sum(p[0] for p in punkte) / len(punkte),
            sum(p[1] for p in punkte) / len(punkte))


def lies_plz(pfad):
    """(land, plz, ort, breite, laenge) je Zeile der Postleitzahldatei."""
    for zeile in pfad.read_text(encoding="utf-8").splitlines():
        teile = zeile.split("\t")
        if len(teile) < 11 or teile[9] == "" or teile[10] == "":
            continue
        try:
            yield teile[0], teile[1], teile[2], float(teile[9]), float(teile[10])
        except ValueError:
            continue


def lies_orte(pfad):
    """(name, breite, laenge, einwohner) je bewohntem Ort des Gesamtauszugs."""
    for zeile in pfad.read_text(encoding="utf-8").splitlines():
        teile = zeile.split("\t")
        # Merkmalsklasse P heisst „bewohnter Ort". Alles andere sind Berge,
        # Flüsse, Bahnhöfe und Verwaltungsgebiete — nichts davon steht als
        # Arbeitsort in einer Anzeige.
        if len(teile) < 15 or teile[6] != "P":
            continue
        try:
            breite, laenge, einwohner = float(teile[4]), float(teile[5]), int(teile[14])
        except ValueError:
            continue

        namen = {teile[1], teile[2]}
        if einwohner >= FREMDNAMEN_AB and teile[3] != "":
            namen.update(teile[3].split(","))

        for name in namen:
            sauber = name.strip()
            # Keine Zeichen, die kein Ortsname hat: Klammern, Ziffern,
            # Schrägstriche. Sie kommen aus Schreibvarianten und Kürzeln.
            if sauber == "" or len(sauber) < 2 or len(sauber) > 60:
                continue
            if any(zeichen.isdigit() for zeichen in sauber):
                continue
            if any(zeichen in sauber for zeichen in "()[]/\\|,;:"):
                continue
            yield sauber, breite, laenge, einwohner


def gruppiere(punkte):
    """Punkte, die näher als STREUUNG_KM beieinanderliegen, in einer Gruppe."""
    gruppen = []
    for punkt in punkte:
        for gruppe in gruppen:
            if entfernung_km(mitte([(p[0], p[1]) for p in gruppe]),
                             (punkt[0], punkt[1])) <= STREUUNG_KM:
                gruppe.append(punkt)
                break
        else:
            gruppen.append([punkt])
    return gruppen


def main():
    quelle = pathlib.Path(sys.argv[1])
    ziel = pathlib.Path(__file__).parent.parent / (
        "src/shared/WorkerTransfer.ServiceDefaults/Postleitzahlen.txt")

    # --- Ortsnamen zuerst ---------------------------------------------------
    #
    # Sie werden GEBRAUCHT, bevor die Postleitzahlen entstehen: nur was auch im
    # Ortsverzeichnis steht, darf eine Postleitzahl benennen. Siehe unten.
    nach_name = {}
    ist_ort = set()

    for land in ("DE", "AT", "CH"):
        for name, breite, laenge, einwohner in lies_orte(
                quelle / f"dump-{land}" / f"{land}.txt"):
            nach_name.setdefault(name, []).append((breite, laenge, einwohner))
            ist_ort.add(name.lower())

    # --- Postleitzahlen -----------------------------------------------------
    nach_plz = {}
    for land in ("DE", "AT", "CH"):
        for zeile in lies_plz(quelle / land / f"{land}.txt"):
            nach_plz.setdefault((zeile[0], zeile[1]), []).append(zeile)

    plz_zeilen = []
    verworfen = 0

    for (land, plz), zeilen in sorted(nach_plz.items()):
        punkte = [(z[3], z[4]) for z in zeilen]
        schwerpunkt = mitte(punkte)

        # Die Grosskunden-Probe. Siehe STREUUNG_KM.
        if max(entfernung_km(schwerpunkt, p) for p in punkte) > STREUUNG_KM:
            verworfen += 1
            continue

        # Der häufigste Ortsname dieser Postleitzahl steht dabei — er löst
        # einen mehrdeutigen vierstelligen Code auf (AT und CH teilen sich den
        # Bereich 1000–9999) und beantwortet die Gegenrichtung „welcher Ort
        # liegt hier?".
        #
        # ER MUSS EIN ORT SEIN. Gemessen: die deutschen Daten führen unter
        # Grosskunden-Postleitzahlen Firmen und Behörden als „Ortsnamen", und
        # solange das durchging, antwortete die Gegenrichtung für Berlin mit
        # „Adam Opel GmbH" und für München mit „Amtsgericht München". Die
        # Streuungsprobe oben fängt das nicht: liegen alle Zeilen in EINER
        # Stadt, streut nichts. Also wird gegen das Ortsverzeichnis geprüft —
        # „Berlin" steht dort, „Adam Opel GmbH" nicht.
        haeufigkeit = {}
        for z in zeilen:
            # Auch der Teil vor dem Komma zählt: Österreich führt Bezirke als
            # „Wien, Innere Stadt", und das steht in keinem Ortsverzeichnis —
            # „Wien" schon. Ohne diese Zeile fiel die Postleitzahl 1010 als
            # angeblicher Grosskunde heraus.
            name = z[2].split(",")[0].strip()

            if z[2].lower() in ist_ort or name.lower() in ist_ort:
                haeufigkeit[name] = haeufigkeit.get(name, 0) + 1

        # Kein einziger echter Ortsname: dann ist der Code kein Ort, sondern
        # ein Empfänger, und er gehört gar nicht in die Tabelle.
        if not haeufigkeit:
            verworfen += 1
            continue

        ort = max(haeufigkeit, key=lambda name: (haeufigkeit[name], -len(name)))

        plz_zeilen.append(
            f"{land}\t{plz}\t{schwerpunkt[0]:.3f}\t{schwerpunkt[1]:.3f}\t{ort}")

    # --- Ortsnamen ----------------------------------------------------------
    # Jeder Name, der eine Postleitzahl bezeichnet, bleibt — auch ein winziger.
    plz_namen = set()
    for zeile in plz_zeilen:
        ort = zeile.split("\t")[4].lower()
        plz_namen.add(ort)
        plz_namen.add(ort.split(",")[0].strip())

    namens_zeilen = []
    mehrdeutig = 0
    zu_klein = 0

    for name, vorkommen in sorted(nach_name.items()):
        gruppen = gruppiere(vorkommen)
        gruppen.sort(key=lambda g: sum(p[2] for p in g), reverse=True)

        # Einer allein gewinnt immer. Sonst muss er die anderen deutlich
        # überragen — siehe UEBERLEGENHEIT.
        if len(gruppen) > 1:
            erste = sum(p[2] for p in gruppen[0])
            zweite = sum(p[2] for p in gruppen[1])
            if erste == 0 or erste < zweite * UEBERLEGENHEIT:
                mehrdeutig += 1
                continue

        if (sum(p[2] for p in gruppen[0]) < MINDEST_EINWOHNER
                and name.lower() not in plz_namen):
            zu_klein += 1
            continue

        schwerpunkt = mitte([(p[0], p[1]) for p in gruppen[0]])
        namens_zeilen.append(
            f"{name}\t{schwerpunkt[0]:.3f}\t{schwerpunkt[1]:.3f}")

    ziel.write_text(
        "# Postleitzahlen und Orte in DE, AT und CH.\n"
        "# ERZEUGT von scripts/plz-tabelle.py — nicht von Hand aendern.\n"
        "# Quelle: GeoNames (download.geonames.org), CC BY 4.0. Siehe NOTICE.md.\n"
        "#\n"
        "# Abschnitt [plz]:  Land, Postleitzahl, Breite, Laenge, Ort\n"
        "# Abschnitt [orte]: Ort, Breite, Laenge\n"
        "[plz]\n" + "\n".join(plz_zeilen) + "\n"
        "[orte]\n" + "\n".join(namens_zeilen) + "\n",
        encoding="utf-8")

    print(f"{len(plz_zeilen)} Postleitzahlen, {len(namens_zeilen)} Orte, "
          f"{verworfen} Grosskunden-Codes, {mehrdeutig} mehrdeutige und "
          f"{zu_klein} zu kleine Namen verworfen, "
          f"{ziel.stat().st_size // 1024} kB")


if __name__ == "__main__":
    main()
