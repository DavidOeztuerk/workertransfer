# Fremde Daten und ihre Lizenzen

Diese Datei nennt Daten Dritter, die **mitgeliefert** werden — nicht die
Programmbibliotheken, die stehen in `Directory.Packages.props` und
`web/package.json`.

## GeoNames — Postleitzahlen und Ortskoordinaten

* **Datei:** `src/shared/WorkerTransfer.ServiceDefaults/Postleitzahlen.txt`
* **Quelle:** [geonames.org](https://www.geonames.org) —
  `download.geonames.org/export/zip/{DE,AT,CH}.zip` (Postleitzahlen) und
  `download.geonames.org/export/dump/{DE,AT,CH}.zip` (Einwohnerzahlen, nur zum
  Auflösen gleichnamiger Orte verwendet)
* **Lizenz:** [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/)
* **Erzeugt von:** `scripts/plz-tabelle.py`
* **Namensnennung:** steht im Impressum der Anwendung (`/impressum`), weil CC BY
  sie gegenüber den Nutzenden verlangt und eine Datei im Quellbaum niemand von
  ihnen sieht.

Warum die Daten mitreisen statt einen Geokodierdienst zu rufen, steht in
[ADR-0032](docs/adr/0032-umkreissuche-ohne-fremden-geokoder.md).

**Erneuern:** die vier Archive nach `<verzeichnis>/{DE,AT,CH}/` bzw.
`<verzeichnis>/dump-{DE,AT,CH}/` entpacken und
`python3 scripts/plz-tabelle.py <verzeichnis>` aufrufen. Das Skript nennt am
Ende, wie viele Postleitzahlen und Orte herauskamen und wie viele Zeilen es
verworfen hat — eine Zahl, die stark abweicht, ist ein Grund nachzusehen und
nicht durchzuwinken.
