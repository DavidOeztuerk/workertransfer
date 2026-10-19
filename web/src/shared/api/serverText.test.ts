import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

/**
 * The server's words are READ, never drawn.
 *
 * <strong>The occasion was measured.</strong> A problem document stays English
 * on the wire and describes a shape (CLAUDE.md, "Frontend"); the UI translates
 * by status. As long as the backend answered in German, a dozen pages drew
 * `result.error.detail` raw and nobody noticed. After the backend translation a
 * German person read "No draft provider is set up." on the advert page — the
 * jobs journey found it, not a unit test.
 *
 * This guard finds the sentence where it is picked up, not where it lands: any
 * read of `.detail`, of an error's `.title`, or of an exception's `.message` in
 * the source. Once a page holds the server's sentence in a variable, no scan can
 * tell where that variable is drawn — so the read itself is the defect.
 *
 * Two kinds of read are right, and each is named below with its reason:
 * the files that BUILD or SORT the sentence, and the calls that hand it to a
 * classifier on the same line. Everything else goes through `failureKey`
 * (`shared/api/errors.ts`) and draws a catalogue key.
 */

// `process.cwd()` is `web/` while Vitest runs.
const ROOT = join(process.cwd(), "src");

function files(path: string): string[] {
  return readdirSync(path).flatMap((entry) => {
    const full = join(path, entry);
    if (statSync(full).isDirectory()) return files(full);
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [full] : [];
  });
}

/** The lines that are code: a name in a comment is prose, not a read. */
function codeLines(content: string): { number: number; line: string }[] {
  return content
    .split("\n")
    .map((line, index) => ({ number: index + 1, line }))
    .filter(({ line }) => !/^\s*(\*|\/\*|\/\/)/.test(line));
}

/**
 * What picks up the server's words. `\.detail\b` does not match `detailKey`:
 * the key is what the UI is meant to read.
 */
const READS = [
  /\.detail\b/,
  /\berror\??\.title\b/,
  /\b(err|error|exception)\??\.message\b/,
];

/** Files that build or sort the sentence — the only places allowed to hold it. */
const SORTERS: Record<string, string> = {
  "/core/api/client.ts": "builds the ApiError from the problem document; draws nothing",
  "/shared/api/errors.ts": "`failureKey` reads the sentence to sort it into a key",
  "/features/work/lib/draftErrors.ts": "`draftStepErrorKey` reads the sentence to sort it into a key",
};

/**
 * A read handed straight to a classifier on the same line is sorted, not
 * drawn: `t(writingKey(result.error.detail))` shows a catalogue text.
 */
const CLASSIFIED = /\b(writingKey|writingReason|emptyLetterHintKey)\([^()]*\.detail\)/;

function reads(): string[] {
  return files(ROOT).flatMap((file) => {
    const name = file.replace(ROOT, "");
    if (name in SORTERS) return [];
    return codeLines(readFileSync(file, "utf8"))
      .filter(({ line }) => {
        // What is handed to a classifier is taken out first; whatever read is
        // left on the line is drawn.
        const rest = line.replace(new RegExp(CLASSIFIED.source, "g"), "");
        return READS.some((pattern) => pattern.test(rest));
      })
      .map(({ number, line }) => `${name}:${number}: ${line.trim()}`);
  });
}

describe("the server's words", () => {
  it("are never picked up outside the files that sort them", () => {
    expect(reads(), "reads of a server sentence — draw t(failureKey(...)) instead").toEqual([]);
  });

  it("are still sorted where the allowlist says so", () => {
    // A stale entry would widen the guard silently: if a sorter moves, its old
    // name must not stay exempt.
    const stale = Object.keys(SORTERS).filter((name) => {
      const content = readFileSync(join(ROOT, name), "utf8");
      return !codeLines(content).some(({ line }) => /\.detail\b/.test(line));
    });

    expect(stale, "allowlisted files that no longer read `.detail`").toEqual([]);
  });

  it("would be found in a page that draws them — the guard can fall", () => {
    // The guard's own counter-probe, kept: the pattern must catch the very
    // line that stood in a dozen pages until 26.09.2026.
    const drawn = "    setError(result.error.detail);";
    const sorted = "        setHint(t(writingKey(result.error.detail)));";

    expect(READS.some((pattern) => pattern.test(drawn))).toBe(true);
    expect(CLASSIFIED.test(sorted)).toBe(true);
    expect(CLASSIFIED.test(drawn)).toBe(false);
  });
});
