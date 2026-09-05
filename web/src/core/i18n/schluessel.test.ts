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

  /**
   * Jede Einsetzung, die ein Text verlangt, wird auch übergeben.
   *
   * <strong>Der Anlass ist gemessen.</strong> Der Lebenslauf zeigte über jeder
   * Station wörtlich <code>Station {{nummer}}</code>, dreimal untereinander. Der
   * Katalog schrieb <code>"Station {{nummer}}"</code>, die Aufrufstelle übergab
   * <code>{ number: … }</code> — ein englischer Name für einen deutschen
   * Platzhalter. i18next füllt einen unbekannten Platzhalter nicht und meckert
   * auch nicht; er bleibt einfach stehen.
   *
   * Der Wächter oben hätte das nie bemerkt: der Schlüssel EXISTIERT ja. Geprüft
   * wird deshalb der Platzhalter, nicht der Schlüssel.
   *
   * Geprüft wird nur, was an derselben Stelle als Objektliteral danebensteht.
   * Wo die Werte aus einer Variablen kommen, schweigt der Wächter — lieber eine
   * Lücke als ein Test, den man mit Umschreiben zum Schweigen bringt.
   */
  it("bekommen jede Einsetzung, die ihr Text verlangt", () => {
    // `t("pfad", { a: …, b: … })` — nur mit unmittelbarem Objektliteral.
    const muster = /\bt\(\s*"([a-z][A-Za-z0-9.]+)"\s*,\s*\{([^{}]*)\}/g;
    const fehlend: string[] = [];

    for (const datei of dateien(WURZEL)) {
      if (datei.includes("/kataloge/")) continue;
      const inhalt = readFileSync(datei, "utf8");

      for (const fund of inhalt.matchAll(muster)) {
        const pfad = fund[1];
        const rumpf = fund[2];
        if (pfad === undefined || rumpf === undefined) continue;

        const text =
          schlage(pfad) ?? schlage(`${pfad}_other`) ?? schlage(`${pfad}_one`);
        if (typeof text !== "string") continue;

        const verlangt = [...text.matchAll(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g)]
          .map((treffer) => treffer[1])
          .filter((name): name is string => name !== undefined);

        // Beide Schreibweisen zählen: `{ nummer: 3 }` UND die Kurzform
        // `{ km }`. Nur nach `name:` zu suchen war der erste Versuch, und er
        // meldete vier Stellen als kaputt, die vollkommen in Ordnung sind.
        const uebergeben = new Set(
          rumpf
            .split(",")
            .map((teil) => /^\s*([A-Za-z0-9_]+)/.exec(teil)?.[1])
            .filter((name): name is string => name !== undefined)
        );

        for (const name of verlangt) {
          if (!uebergeben.has(name)) {
            fehlend.push(`${datei.replace(WURZEL, "")}: ${pfad} verlangt {{${name}}}`);
          }
        }
      }
    }

    expect(fehlend, "Platzhalter ohne übergebenen Wert").toEqual([]);
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
