import { describe, expect, it } from "vitest";

import { buildExport, exportFilename, section } from "./build";

const NOW = new Date("2026-08-02T12:00:00Z");

describe("section", () => {
  it("keeps a failed section in the file instead of dropping it", () => {
    // Weggelassen sähe die Datei vollständig aus — und jemand würde daraus
    // schließen, es gebe nichts weiter über ihn.
    expect(section(false, undefined)).toEqual({ status: "nicht_abrufbar" });
  });

  it("never carries data for a section that failed", () => {
    expect(section(false, { heimlich: "da" })).not.toHaveProperty("daten");
  });
});

describe("buildExport", () => {
  it("names what is missing, at the top", () => {
    const result = buildExport(
      {
        profil: section(true, { headline: "x" }),
        portfolio: section(false, undefined),
        lebenslauf: section(false, undefined),
      },
      NOW
    );

    expect(result.unvollständig).toEqual(["portfolio", "lebenslauf"]);
  });

  it("says plainly when nothing is missing", () => {
    const result = buildExport({ profil: section(true, null) }, NOW);

    expect(result.unvollständig).toEqual([]);
  });

  it("stamps when it was made — an export without a date says nothing", () => {
    expect(buildExport({}, NOW).erzeugt_am).toBe("2026-08-02T12:00:00.000Z");
  });

  it("keeps every section, including the empty ones", () => {
    // „Kein Lebenslauf" ist eine Auskunft. Sie fehlt sonst.
    const result = buildExport({ lebenslauf: section(true, null) }, NOW);

    expect(result.abschnitte.lebenslauf).toEqual({ status: "ok", daten: null });
  });
});

describe("exportFilename", () => {
  it("carries the date, so two exports do not overwrite each other", () => {
    expect(exportFilename(NOW)).toBe("workertransfer-meine-daten-2026-08-02.json");
  });
});
