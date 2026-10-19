/**
 * The data export — as a file, and built exclusively in the browser.
 *
 * <strong>It is stored nowhere.</strong> There is no endpoint that produces it
 * and no file left lying on a server — otherwise an export of one's own data
 * would itself have left data behind.
 *
 * <strong>The property names below are the FILE's keys and stay German</strong>
 * (`erzeugt_am`, `unvollständig`, `abschnitte`, `daten`, `nicht_abrufbar`): the
 * file is a format a person downloads and keeps, and it has carried these keys
 * since the first export. Like a wire field name, renaming one here would change
 * the file, not the code.
 */
export type SectionStatus = "ok" | "nicht_abrufbar";

export interface ExportSection {
  status: SectionStatus;
  daten?: unknown;
}

export interface DataExport {
  erzeugt_am: string;
  /** Which sections are missing. Empty means: nothing is missing. */
  unvollständig: string[];
  abschnitte: Record<string, ExportSection>;
}

/**
 * A section, and whether it is really there.
 *
 * <strong>If it is missing, no data stands in it either.</strong> A section
 * marked `nicht_abrufbar` with a filled field anyway would be the worst variant:
 * half true, and nobody sees which half.
 */
export const section = (ok: boolean, data: unknown): ExportSection =>
  ok ? { status: "ok", daten: data } : { status: "nicht_abrufbar" };

/**
 * Builds the export and states at the top what is missing.
 *
 * The list of gaps stands IN the file, not only on the page: whoever opens it
 * later should see without the page that it is incomplete.
 */
export function buildExport(
  sections: Record<string, ExportSection>,
  now: Date = new Date()
): DataExport {
  return {
    erzeugt_am: now.toISOString(),
    unvollständig: Object.entries(sections)
      .filter(([, value]) => value.status !== "ok")
      .map(([name]) => name),
    abschnitte: sections,
  };
}

/**
 * A file name that still says something in a downloads folder. The name is
 * pinned by the E2E journey (`consents-journey.spec.ts`) and stays as it is.
 */
export const fileName = (now: Date = new Date()): string =>
  `workertransfer-meine-daten-${now.toISOString().slice(0, 10)}.json`;
