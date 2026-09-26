import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";

import {
  EMPLOYMENT_TYPES,
  REMOTE_MODES,
  employmentLabel,
  remoteLabel,
} from "../../features/work/api/jobs";
import { OCCUPATIONAL_FIELDS } from "../../shared/lib/occupationalFields";
import { LANGUAGES } from "../store/preferencesSlice";
import de from "./catalogs/de";
import { i18n } from "./i18n";

/**
 * A catalogue key must never be passed on as finished text.
 *
 * <strong>The occasion was measured, not imagined.</strong> The writing help
 * carried `message: "fehler.entwurfNichtVerfuegbarLang"` (today
 * `errors.draftHelpUnavailableLong`) — and the UI showed the key verbatim in
 * the warning box, because i18next hands an unknown key back unchanged. It
 * looked like text, it was none, and no type and no test run noticed: the E2E
 * journey found it, seven minutes later.
 *
 * Hence this guard. It separates the two kinds of sites:
 *
 * - A key in an <c>interpretError</c> table (<c>titleKey:</c>,
 *   <c>detailKey:</c>) or as the fallback of <c>request(...)</c> is RIGHT —
 *   both are looked up, and at the answer rather than when the module loads.
 * - A key in a field the UI draws directly (<c>meldung:</c>, <c>message:</c>,
 *   <c>detail:</c>, <c>title:</c>) is a defect as long as no <c>i18n.t</c>
 *   stands beside it.
 */

// `process.cwd()` is `web/` while Vitest runs — `import.meta.url` does not
// point at the file system in the test run's browser environment.
const ROOT = join(process.cwd(), "src");

/** Fields whose content lands in the UI unchecked. */
const DISPLAY_FIELDS = ["meldung", "message", "detail", "title", "hint", "lead", "label"];

/** The catalogue's sections — the first segment of every key. */
const SECTIONS = new Set(Object.keys(de));

function files(path: string): string[] {
  return readdirSync(path).flatMap((entry) => {
    const full = join(path, entry);
    if (statSync(full).isDirectory()) return files(full);
    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [full] : [];
  });
}

/** The source files that use keys — the catalogues themselves excluded. */
function sources(): { file: string; content: string }[] {
  return files(ROOT)
    .filter((file) => !file.includes("/catalogs/"))
    .map((file) => ({ file: file.replace(ROOT, ""), content: readFileSync(file, "utf8") }));
}

/** The lines that are code: a key named in a comment is prose, not a use. */
function codeLines(content: string): string[] {
  return content.split("\n").filter((line) => !/^\s*(\*|\/\*|\/\/)/.test(line));
}

/** Reads a dotted path from the catalogue. `undefined` if it does not exist. */
function lookUp(path: string): unknown {
  return path
    .split(".")
    .reduce<unknown>(
      (value, part) =>
        typeof value === "object" && value !== null
          ? (value as Record<string, unknown>)[part]
          : undefined,
      de
    );
}

/** A key exists when it names a TEXT — a section is not one, and plurals count by their `_other`. */
function exists(path: string): boolean {
  return typeof lookUp(path) === "string" || typeof lookUp(`${path}_other`) === "string";
}

/** Reads a source file below `src/`. */
function source(path: string): string {
  return readFileSync(join(ROOT, path), "utf8");
}

/**
 * The values of a literal array constant, read from the file that declares it —
 * `const NAME = [ … ]` — so the test sees the site's own list, not a copy.
 */
function constantArray(path: string, name: string): unknown[] {
  const found = new RegExp(`const ${name}\\b[^=]*=\\s*(\\[[^\\]]*\\])`).exec(source(path));
  if (found?.[1] === undefined) throw new Error(`${path}: no array constant ${name}`);
  return new Function(`return ${found[1]};`)() as unknown[];
}

/** The string members of a union type — `export type NAME = "a" | "b";` — read from its file. */
function unionMembers(path: string, name: string): string[] {
  const found = new RegExp(`export type ${name}\\s*=([^;]*);`).exec(source(path));
  if (found?.[1] === undefined) throw new Error(`${path}: no union type ${name}`);
  return [...found[1].matchAll(/"([^"]*)"/g)].map((member) => member[1] ?? "");
}

/**
 * Evaluates a key template exactly as it stands in the source, once per scope.
 * `\`profile.commute_\${readiness}\`` with `{ readiness: "egal" }` gives
 * `profile.commute_egal`.
 */
function evaluate(template: string, scopes: Record<string, unknown>[]): string[] {
  return scopes.map((scope) =>
    String(new Function(...Object.keys(scope), `return \`${template}\`;`)(...Object.values(scope)))
  );
}

/** The keys a function hands to `i18n.t`, captured while it runs. */
function captured(run: () => unknown): string[] {
  const spy = vi.spyOn(i18n, "t");
  try {
    run();
    return spy.mock.calls.map((call) => String(call[0]));
  } finally {
    spy.mockRestore();
  }
}

/** One scope per value, under the name the site's template reads. */
const each = (name: string, values: unknown[]) => values.map((value) => ({ [name]: value }));
const nested = (outer: string, inner: string, values: unknown[]) =>
  values.map((value) => ({ [outer]: { [inner]: value } }));

const PAGES = "features/person/pages";
const WORK = "features/work";

/**
 * Every key the source BUILDS instead of naming — by its template text, exactly
 * as it stands in the code, with every value its variable part can take.
 *
 * The values come from where the site gets them: a constant in the same file,
 * a union type, an exported list. Only two are written out here, because the
 * code holds them nowhere: the advisor stage is a number from the server (0–3,
 * ADR-0037), and the attendance word is typed `string` on the wire (the three
 * words of scout-service's `Availability.Word`).
 */
const DYNAMIC: Record<string, () => Record<string, unknown>[] | string[]> = {
  "deleteAccount.deleted${number}": () =>
    each("number", constantArray(`${PAGES}/DeleteAccountPage.tsx`, "WHAT_DISAPPEARS")),
  "profile.commute_${readiness}": () =>
    each("readiness", constantArray(`${PAGES}/ProfilePage.tsx`, "COMMUTE_READINESS")),
  "profile.relocate_${value}": () =>
    each("value", constantArray(`${PAGES}/ProfilePage.tsx`, "RELOCATION_READINESS")),
  "profile.commute_${treffer.reach_commute}": () =>
    nested("treffer", "reach_commute", unionMembers("features/person/api/profile.ts", "CommuteReadiness")),
  "resume.template${templ.charAt(0).toUpperCase()}${templ.slice(1)}": () =>
    each("templ", constantArray(`${PAGES}/ResumePage.tsx`, "TEMPLATES")),
  "resume.template${templ.charAt(0).toUpperCase()}${templ.slice(1)}Hint": () =>
    each("templ", constantArray(`${PAGES}/ResumePage.tsx`, "TEMPLATES")),
  "home.principle${number}Title": () =>
    each("number", constantArray("features/public/pages/HomePage.tsx", "grundlagen")),
  "home.principle${number}Text": () =>
    each("number", constantArray("features/public/pages/HomePage.tsx", "grundlagen")),
  "drafts.${WritingReason}": () =>
    each("WritingReason", unionMembers(`${WORK}/lib/draftErrors.ts`, "WritingReason")),
  "drafts.${writingReason(error)}": () =>
    unionMembers(`${WORK}/lib/draftErrors.ts`, "WritingReason").map((error) => ({
      writingReason: (reason: string) => reason,
      error,
    })),
  "drafts.${reason}": () =>
    each("reason", unionMembers(`${WORK}/lib/draftErrors.ts`, "WritingReason")),
  "drafts.status.${status}": () =>
    each("status", unionMembers(`${WORK}/api/applications.ts`, "DraftStatus")),
  "drafts.statusHint.${status}": () =>
    each("status", unionMembers(`${WORK}/api/applications.ts`, "DraftStatus")),
  // The two job labels look a word up in a local table; the real function runs.
  "job.remote${key[value]}": () => captured(() => REMOTE_MODES.map(remoteLabel)),
  "job.employment${key[value]}": () => captured(() => EMPLOYMENT_TYPES.map(employmentLabel)),
  // `caseItem` and `conversation` are the person's pages (features/work),
  // `vorgang` and `gespraech` the company's (features/company) — two sites, one
  // union each.
  "assessments.state_${caseItem.state}": () =>
    nested("caseItem", "state", unionMembers(`${WORK}/api/assessments.ts`, "CaseState")),
  "assessments.state_${vorgang.state}": () =>
    nested("vorgang", "state", unionMembers(`${WORK}/api/assessments.ts`, "CaseState")),
  "assessments.outcome_${caseItem.evaluation.outcome}": () =>
    unionMembers(`${WORK}/api/assessments.ts`, "Outcome").map((outcome) => ({
      caseItem: { evaluation: { outcome } },
    })),
  "assessments.outcome_${vorgang.evaluation.outcome}": () =>
    unionMembers(`${WORK}/api/assessments.ts`, "Outcome").map((outcome) => ({
      vorgang: { evaluation: { outcome } },
    })),
  "advisor.state_${conversation.state}": () =>
    nested("conversation", "state", unionMembers(`${WORK}/api/advisor.ts`, "ConversationState")),
  "advisor.state_${gespraech.state}": () =>
    nested("gespraech", "state", unionMembers(`${WORK}/api/advisor.ts`, "ConversationState")),
  "advisor.stage${conversation.stage}Short": () =>
    nested("conversation", "stage", [0, 1, 2, 3]),
  "advisor.stage${gespraech.stage}Short": () => nested("gespraech", "stage", [0, 1, 2, 3]),
  "advisor.stage${Math.min(conversation.stage + 1, 3)}": () =>
    nested("conversation", "stage", [0, 1, 2, 3]),
  "scout.attendance_${treffer.reach_attendance}": () =>
    nested("treffer", "reach_attendance", ["remote", "hybrid", "vor_ort"]),
  "scout.reach_${treffer.reach}": () =>
    nested("treffer", "reach", unionMembers("features/company/api/scout.ts", "Erreichbarkeit")),
  "applications.status${zeile.status.charAt(0).toUpperCase()}${zeile.status.slice(1)}": () =>
    nested("zeile", "status", unionMembers(`${WORK}/api/applications.ts`, "ApplicationStatus")),
  "occupation.field.${field}": () => each("field", [...OCCUPATIONAL_FIELDS]),
  "language.${language}": () => each("language", [...LANGUAGES]),
  // A type derived from the catalogue itself: tsc checks every use of it.
  'errors.${keyof Catalog["errors"] & string}': () =>
    Object.keys(de.errors).map((key) => `errors.${key}`),
};

/** The keys one dynamic site can produce. */
function producedBy(template: string): string[] {
  const produce = DYNAMIC[template];
  if (produce === undefined) return [];
  const result = produce();
  return result.every((entry) => typeof entry === "string")
    ? (result as string[])
    : evaluate(template, result as Record<string, unknown>[]);
}

/** Every template in the code that builds a key: a backtick string starting with a section. */
function dynamicSites(): { file: string; template: string }[] {
  return sources().flatMap(({ file, content }) =>
    codeLines(content).flatMap((line) =>
      [...line.matchAll(/`([a-z][A-Za-z0-9]*)\.([^`]*\$\{[^`]*)`/g)]
        .filter((found) => SECTIONS.has(found[1] ?? ""))
        .map((found) => ({ file, template: `${found[1]}.${found[2]}` }))
    )
  );
}

/** Key-shaped string literals whose first segment is a section, but which are not keys. */
const NOT_KEYS = new Map([
  // A MUI palette path in an `sx` prop — `common` is a section name as well.
  ["common.white", "MUI palette path"],
]);

describe("catalogue keys", () => {
  /**
   * <strong>Every key the code names must exist.</strong>
   *
   * The occasion was measured: a rename across the frontend renamed the keys in
   * the CATALOGUE — there they are object names, that is, code — while the uses
   * are strings and stayed. All three languages were renamed alike, the type
   * stayed valid, the catalogue test stayed green, and the UI showed
   * `registrierung.erneut` as text.
   *
   * This test closes exactly that gap: it compares the USED keys against the
   * catalogue, not the catalogues against each other. Keys the code builds are
   * the business of the dynamic-site tests below.
   */
  it("exist in the catalogue", () => {
    const pattern = /(?:\bt\(\s*"([a-z][A-Za-z0-9_.]+)"|i18nKey="([a-z][A-Za-z0-9_.]+)")/g;
    const missing: string[] = [];

    for (const { file, content } of sources()) {
      for (const found of content.matchAll(pattern)) {
        const path = found[1] ?? found[2];
        if (path === undefined || !path.includes(".")) continue;
        if (!exists(path)) missing.push(`${file}: ${path}`);
      }
    }

    expect(missing, "used keys without a catalogue entry").toEqual([]);
  });

  /**
   * A key does not have to stand inside `t(…)` to be one: status tables,
   * navigation entries and `interpretError` tables hold keys that a
   * `t(variable)` reads later. Every string literal that starts with a
   * catalogue section and looks like a path must therefore name a text — or
   * stand in `NOT_KEYS` with its reason.
   */
  it("name a text wherever a literal looks like a key", () => {
    const pattern = /(["'`])([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9_]+)+)\1/g;
    const missing: string[] = [];

    for (const { file, content } of sources()) {
      for (const line of codeLines(content)) {
        for (const found of line.matchAll(pattern)) {
          const path = found[2] ?? "";
          if (!SECTIONS.has(path.split(".")[0] ?? "") || NOT_KEYS.has(path)) continue;
          if (!exists(path)) missing.push(`${file}: ${path}`);
        }
      }
    }

    expect(missing, "key-shaped literals without a catalogue entry").toEqual([]);
  });

  /**
   * <strong>Every key the code BUILDS is known here.</strong>
   *
   * The first test sees only keys that stand whole in the source. A template
   * such as `profile.commute_${readiness}` is invisible to it — and the English
   * rename of the catalogue renamed exactly these prefixes in one pass. So every
   * template in the code must stand in `DYNAMIC`, with the values its variable
   * part can take; a new template that nobody lists makes this red instead of
   * going unchecked.
   */
  it("that the code builds are all listed with their values", () => {
    const sites = dynamicSites();
    const templates = new Set(sites.map((site) => site.template));

    expect(sites.length, "otherwise the scan finds no dynamic site at all").toBeGreaterThan(10);

    const unlisted = sites
      .filter((site) => !(site.template in DYNAMIC))
      .map((site) => `${site.file}: ${site.template}`);
    const stale = Object.keys(DYNAMIC).filter((template) => !templates.has(template));

    expect(unlisted, "templates in the code without an entry in DYNAMIC").toEqual([]);
    expect(stale, "entries in DYNAMIC whose template no longer stands in the code").toEqual([]);
  });

  it("that the code builds all name a text", () => {
    const missing: string[] = [];

    for (const template of Object.keys(DYNAMIC)) {
      const produced = producedBy(template);
      if (produced.length === 0) missing.push(`${template}: produces no key at all`);
      for (const key of produced) {
        if (!exists(key)) missing.push(`${template} → ${key}`);
      }
    }

    expect(missing, "built keys without a catalogue entry").toEqual([]);
  });

  /**
   * Every placeholder a text asks for is also passed.
   *
   * <strong>The occasion was measured.</strong> The CV showed
   * <code>Station {{nummer}}</code> verbatim above every position, three times
   * in a row. The catalogue wrote <code>"Station {{nummer}}"</code>, the call
   * site passed <code>{ number: … }</code> — an English name for a German
   * placeholder. i18next does not fill an unknown placeholder and does not
   * complain either; it simply stays.
   *
   * The guards above would never have noticed: the key EXISTS. So the
   * placeholder is checked, not the key.
   *
   * Only what stands beside it as an object literal at the same site is
   * checked. Where the values come from a variable the guard stays silent —
   * better a gap than a test one silences by rewriting.
   */
  it("get every placeholder their text asks for", () => {
    // `t("path", { a: …, b: … })` — only with an immediate object literal.
    const pattern = /\bt\(\s*"([a-z][A-Za-z0-9_.]+)"\s*,\s*\{([^{}]*)\}/g;
    const missing: string[] = [];

    for (const { file, content } of sources()) {
      for (const found of content.matchAll(pattern)) {
        const path = found[1];
        const body = found[2];
        if (path === undefined || body === undefined) continue;

        const text = lookUp(path) ?? lookUp(`${path}_other`) ?? lookUp(`${path}_one`);
        if (typeof text !== "string") continue;

        const asked = [...text.matchAll(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g)]
          .map((hit) => hit[1])
          .filter((name): name is string => name !== undefined);

        // Both spellings count: `{ nummer: 3 }` AND the shorthand `{ km }`.
        // Searching only for `name:` was the first attempt, and it reported four
        // sites as broken that are perfectly fine.
        const passed = new Set(
          body
            .split(",")
            .map((part) => /^\s*([A-Za-z0-9_]+)/.exec(part)?.[1])
            .filter((name): name is string => name !== undefined)
        );

        for (const name of asked) {
          if (!passed.has(name)) missing.push(`${file}: ${path} asks for {{${name}}}`);
        }
      }
    }

    expect(missing, "placeholders without a passed value").toEqual([]);
  });

  it("never stand raw in a field that is drawn", () => {
    const pattern = new RegExp(`\\b(${DISPLAY_FIELDS.join("|")}):\\s*"(errors\\.[A-Za-z]+)"`, "g");
    const hits: string[] = [];

    for (const { file, content } of sources()) {
      for (const found of content.matchAll(pattern)) {
        hits.push(`${file}: ${found[0]}`);
      }
    }

    expect(hits, "keys without i18n.t in a display field").toEqual([]);
  });
});
