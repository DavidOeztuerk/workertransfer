import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

// Über `process.cwd()` und nicht über `import.meta.url`: unter Vitests
// jsdom-Umgebung ist `import.meta.url` KEINE `file:`-URL, und `fileURLToPath`
// stirbt daran, bevor eine einzige Behauptung geprüft wurde. Vitest läuft im
// Paketverzeichnis — sowohl bei `pnpm --filter` als auch unter turbo.
//
// Diese Auflösung hängt damit am Arbeitsverzeichnis. Läuft sie einmal
// woanders, schlägt der erste Test („reads … at all") laut zu, statt still
// null Vergleiche zu machen. Genau dafür ist er da.
const STYLES_DIR = join(process.cwd(), "src", "styles");
const TOKENS_FILE = join(STYLES_DIR, "tokens.css");

function cssTexts(): { name: string; text: string }[] {
  return readdirSync(STYLES_DIR)
    .filter((name) => name.endsWith(".css"))
    .map((name) => ({ name, text: readFileSync(join(STYLES_DIR, name), "utf8") }));
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

describe("die Palette des Design-Systems", () => {
  // Zuerst: der Wächter muss überhaupt etwas lesen. Ohne diese Prüfung
  // schrumpft er bei einer Umbenennung des Ordners still auf null Vergleiche
  // und meldet „alles in Ordnung" über Dateien, die er nicht mehr liest —
  // dieselbe Fehlerklasse wie ein E2E-Lauf, der sich vollständig überspringt
  // und trotzdem grün berichtet. Dasselbe Muster wie in
  // tests/test_roadmap_status_is_consistent.py.
  it("reads the token file and the component styles at all", () => {
    const files = cssTexts();
    const tokens = definedTokens(readFileSync(TOKENS_FILE, "utf8"));

    expect(files.length).toBeGreaterThanOrEqual(6);
    expect(tokens.size).toBeGreaterThanOrEqual(25);
    expect(files.flatMap((file) => tokenUsages(file.text)).length).toBeGreaterThanOrEqual(20);
  });

  it("defines every token it uses", () => {
    const tokens = definedTokens(readFileSync(TOKENS_FILE, "utf8"));

    const undefinedUses = cssTexts().flatMap((file) =>
      tokenUsages(file.text)
        .filter((use) => !tokens.has(use.token))
        .map((use) => `${file.name}: ${use.token}`)
    );

    expect(undefinedUses).toEqual([]);
  });

  // Ein Fallback ist genau der Weg, auf dem hier zwei Paletten entstanden sind:
  // var(--wt-accent, #1f6f5c) sah aus wie ein Token und war ein zweiter
  // Grünton. Ist das Token definiert, ist der Fallback toter Code mit einer
  // Meinung — und die kommt zurück, sobald jemand ein Token umbenennt.
  it("uses no fallback values, because a fallback is a second palette", () => {
    const withFallback = cssTexts().flatMap((file) =>
      tokenUsages(file.text)
        .filter((use) => use.hasFallback)
        .map((use) => `${file.name}: ${use.token}`)
    );

    expect(withFallback).toEqual([]);
  });

  it("keeps all token definitions in one file", () => {
    const strays = cssTexts()
      .filter((file) => file.name !== "tokens.css")
      .filter((file) => definedTokens(file.text).size > 0)
      .map((file) => file.name);

    expect(strays).toEqual([]);
  });
});
