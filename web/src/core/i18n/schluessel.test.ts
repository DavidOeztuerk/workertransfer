import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

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

function dateien(pfad: string): string[] {
  return readdirSync(pfad).flatMap((eintrag) => {
    const voll = join(pfad, eintrag);
    if (statSync(voll).isDirectory()) return dateien(voll);
    return /\.tsx?$/.test(eintrag) && !/\.test\.tsx?$/.test(eintrag) ? [voll] : [];
  });
}

describe("Katalogschlüssel", () => {
  it("stehen nie roh in einem Feld, das gezeichnet wird", () => {
    const muster = new RegExp(`\\b(${ANZEIGEFELDER.join("|")}):\\s*"(fehler\\.[A-Za-z]+)"`, "g");
    const treffer: string[] = [];

    for (const datei of dateien(WURZEL)) {
      const inhalt = readFileSync(datei, "utf8");
      for (const fund of inhalt.matchAll(muster)) {
        treffer.push(`${datei.replace(WURZEL, "")}: ${fund[0]}`);
      }
    }

    expect(treffer, "Schlüssel ohne i18n.t in einem Anzeigefeld").toEqual([]);
  });
});
