import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

// apps/web hängt von packages/ui ab, nie umgekehrt — deshalb steht diese
// Prüfung hier und nicht im Paket. Sie liest die Definitionen des Pakets und
// die Verwendungen dieser Anwendung.
//
// Über `process.cwd()` und nicht über `import.meta.url`: unter Vitests
// jsdom-Umgebung ist `import.meta.url` KEINE `file:`-URL, und `fileURLToPath`
// stirbt daran, bevor eine einzige Behauptung geprüft wurde.
const OWN_CSS = join(process.cwd(), "src", "styles.css");
const UI_TOKENS = join(process.cwd(), "..", "..", "packages", "ui", "src", "styles", "tokens.css");

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

describe("apps/web/src/styles.css gegen die Tokens des Design-Systems", () => {
  // Dieselbe Selbstprüfung wie im Paket: findet dieser Test seine Dateien
  // nicht, soll er das sagen und nicht null Vergleiche als Erfolg melden.
  it("reads both files at all", () => {
    expect(definedTokens(readFileSync(UI_TOKENS, "utf8")).size).toBeGreaterThanOrEqual(25);
    expect(tokenUsages(readFileSync(OWN_CSS, "utf8")).length).toBeGreaterThanOrEqual(20);
  });

  it("uses only tokens the design system defines", () => {
    const tokens = definedTokens(readFileSync(UI_TOKENS, "utf8"));

    const unknown = tokenUsages(readFileSync(OWN_CSS, "utf8"))
      .filter((use) => !tokens.has(use.token))
      .map((use) => use.token);

    expect([...new Set(unknown)]).toEqual([]);
  });

  // 21 Stellen trugen einen Fallback, und die Fallbackwerte waren eine zweite
  // Palette. Solange der Fallback dasteht, kommt sie zurück, sobald jemand ein
  // Token umbenennt — still, denn es sieht ja weiterhin gut aus.
  it("carries no fallback values", () => {
    const withFallback = tokenUsages(readFileSync(OWN_CSS, "utf8"))
      .filter((use) => use.hasFallback)
      .map((use) => use.token);

    expect([...new Set(withFallback)]).toEqual([]);
  });
});
