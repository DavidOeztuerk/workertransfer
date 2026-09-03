import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

import de from "./kataloge/de";

/**
 * Ein Katalogschlüssel darf nirgends als fertiger Text durchgereicht werden.
 *
 * <strong>Der Anlass ist gemessen, nicht ausgedacht.</strong> In der
 * Formulierungshilfe stand `message: "fehler.entwurfNichtVerfuegbarLang"` — und
 * die Oberfläche zeigte den Schlüssel wörtlich im Warnkasten, weil i18next einen
 * unbekannten Schlüssel unverändert zurückgibt. Es sah aus wie ein Text, es war
 * keiner, und kein Typ und kein Testlauf hat es bemerkt: gefunden hat es die
 * E2E-Reise, sieben Minuten später.
 *
 * Deshalb dieser Wächter. Er trennt die beiden Sorten von Stellen:
 *
 * - Ein Schlüssel in einer <c>deuten</c>-Tabelle (<c>titel:</c>, <c>text:</c>)
 *   oder als Ersatztext von <c>request(...)</c> ist RICHTIG — beide werden
 *   nachgeschlagen, und zwar bei der Antwort statt beim Laden des Moduls.
 * - Ein Schlüssel in einem Feld, das die Oberfläche direkt zeichnet
 *   (<c>meldung:</c>, <c>message:</c>, <c>detail:</c>, <c>title:</c>), ist ein
 *   Fehler, solange <c>i18n.t</c> nicht danebensteht.
 */

// `process.cwd()` ist `web/`, wenn Vitest laeuft — `import.meta.url` zeigt in
// der Browserumgebung des Testlaufs nicht auf das Dateisystem.
const WURZEL = join(process.cwd(), "src");

/** Felder, deren Inhalt ungeprüft in der Oberfläche landet. */
const ANZEIGEFELDER = ["meldung", "message", "detail", "title", "hint", "lead", "label"];

function dateien(path: string): string[] {
  return readdirSync(path).flatMap((entry) => {
    const voll = join(path, entry);
    if (statSync(voll).isDirectory()) return dateien(voll);
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [voll] : [];
  });
}

/** Liest einen Punktpfad aus dem Katalog. `undefined`, wenn es ihn nicht gibt. */
function schlage(pfad: string): unknown {
  return pfad
    .split(".")
    .reduce<unknown>(
      (wert, teil) =>
        typeof wert === "object" && wert !== null
          ? (wert as Record<string, unknown>)[teil]
          : undefined,
      de
    );
}

describe("Katalogschlüssel", () => {
  /**
   * <strong>Jeder Schlüssel, den der Code benutzt, muss es geben.</strong>
   *
   * Der Anlass ist gemessen: eine Umbenennung quer durch das Frontend hat die
   * Schlüssel im KATALOG umbenannt — dort stehen sie als Objektnamen, also als
   * Code — und die Verwendungen stehen in Zeichenketten und blieben. Alle drei
   * Sprachen wurden gleich umbenannt, der Typ blieb gültig, der Katalogtest
   * blieb grün, und in der Oberfläche stand `registrierung.erneut` als Text.
   *
   * Genau diese Lücke schliesst der Test: er vergleicht die BENUTZTEN
   * Schlüssel gegen den Katalog, nicht die Kataloge untereinander.
   *
   * Ausgenommen sind zusammengesetzte Schlüssel (`t(\`start.grundlage${n}\`)`)
   * und solche, die als Variable durchgereicht werden — die stehen hier nicht
   * als Literal und sind mit einem Regex nicht zu greifen. Die Kataloge selbst
   * und die Beugungsformen `_one`/`_other` ebenso.
   */
  it("gibt es alle im Katalog", () => {
    const muster = /(?:\bt\(\s*"([a-z][A-Za-z0-9.]+)"|i18nKey="([a-z][A-Za-z0-9.]+)")/g;
    const fehlend: string[] = [];

    for (const datei of dateien(WURZEL)) {
      if (datei.includes("/kataloge/")) continue;
      const inhalt = readFileSync(datei, "utf8");
      for (const fund of inhalt.matchAll(muster)) {
        const pfad = fund[1] ?? fund[2];
        if (pfad === undefined || !pfad.includes(".")) continue;
        if (schlage(pfad) === undefined && schlage(`${pfad}_other`) === undefined) {
          fehlend.push(`${datei.replace(WURZEL, "")}: ${pfad}`);
        }
      }
    }

    expect(fehlend, "benutzte Schlüssel ohne Eintrag im Katalog").toEqual([]);
  });

  it("stehen nie roh in einem Feld, das gezeichnet wird", () => {
    const muster = new RegExp(`\\b(${ANZEIGEFELDER.join("|")}):\\s*"(fehler\\.[A-Za-z]+)"`, "g");
    const treffer: string[] = [];

    for (const file of dateien(WURZEL)) {
      const content = readFileSync(file, "utf8");
      for (const fund of content.matchAll(muster)) {
        treffer.push(`${file.replace(WURZEL, "")}: ${fund[0]}`);
      }
    }

    expect(treffer, "Schlüssel ohne i18n.t in einem Anzeigefeld").toEqual([]);
  });
});
