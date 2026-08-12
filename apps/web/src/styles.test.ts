import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

// apps/web hängt von packages/ui ab, nie umgekehrt — deshalb steht diese
// Prüfung hier und nicht im Paket. Sie liest die Definitionen des Pakets und
// die Verwendungen dieser Anwendung.
//
// Über `process.cwd()` und nicht über `import.meta.url`: unter Vitests
// jsdom-Umgebung ist `import.meta.url` KEINE `file:`-URL, und `fileURLToPath`
// stirbt daran, bevor eine einzige Behauptung geprüft wurde.
const SRC = join(process.cwd(), "src");
const UI_TOKENS = join(process.cwd(), "..", "..", "packages", "ui", "src", "styles", "tokens.css");

/**
 * ALLE Stylesheets dieser Anwendung, nicht nur `styles.css`.
 *
 * Vorher stand hier ein einzelner Pfad. Das ging so lange gut, wie es eine
 * Datei gab — und riss in dem Moment ein Loch, in dem Regeln zu ihrer Route
 * wanderten: die neue Datei wäre ungeprüft gewesen, und zwar genau in dem
 * Punkt, für den dieser Wächter existiert. Eine Suche statt eines Pfades kann
 * das nicht passieren.
 */
function stylesheets(dir: string = SRC): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) found.push(...stylesheets(path));
    else if (entry.name.endsWith(".css")) found.push(path);
  }
  return found;
}

function definedTokens(text: string): Set<string> {
  const found = new Set<string>();
  for (const match of text.matchAll(/(--wt-[a-z0-9-]+)\s*:/g)) found.add(match[1]!);
  return found;
}

function tokenUsages(text: string): { token: string; hasFallback: boolean }[] {
  const uses: { token: string; hasFallback: boolean }[] = [];
  for (const match of text.matchAll(/var\(\s*(--wt-[a-z0-9-]+)\s*(,)?/g)) {
    uses.push({ token: match[1]!, hasFallback: match[2] === "," });
  }
  return uses;
}

/** Jede Verwendung in jeder Datei, mit dem Dateinamen für die Fehlermeldung. */
function allUsages(): { file: string; token: string; hasFallback: boolean }[] {
  return stylesheets().flatMap((file) =>
    tokenUsages(readFileSync(file, "utf8")).map((use) => ({ file, ...use }))
  );
}

describe("die Stylesheets von apps/web gegen die Tokens des Design-Systems", () => {
  // Dieselbe Selbstprüfung wie im Paket: findet dieser Test seine Dateien
  // nicht, soll er das sagen und nicht null Vergleiche als Erfolg melden.
  it("reads every stylesheet at all", () => {
    expect(definedTokens(readFileSync(UI_TOKENS, "utf8")).size).toBeGreaterThanOrEqual(25);
    // Mehr als eine Datei: sonst prüfte diese Reihe wieder nur `styles.css`,
    // ohne dass jemand es merkt.
    expect(stylesheets().length).toBeGreaterThanOrEqual(2);
    expect(allUsages().length).toBeGreaterThanOrEqual(20);
  });

  it("uses only tokens the design system defines", () => {
    const tokens = definedTokens(readFileSync(UI_TOKENS, "utf8"));

    const unknown = allUsages()
      .filter((use) => !tokens.has(use.token))
      .map((use) => `${use.token} (${use.file.replace(SRC, "src")})`);

    expect([...new Set(unknown)]).toEqual([]);
  });

  // 21 Stellen trugen einen Fallback, und die Fallbackwerte waren eine zweite
  // Palette. Solange der Fallback dasteht, kommt sie zurück, sobald jemand ein
  // Token umbenennt — still, denn es sieht ja weiterhin gut aus.
  it("carries no fallback values", () => {
    const withFallback = allUsages()
      .filter((use) => use.hasFallback)
      .map((use) => `${use.token} (${use.file.replace(SRC, "src")})`);

    expect([...new Set(withFallback)]).toEqual([]);
  });
});
