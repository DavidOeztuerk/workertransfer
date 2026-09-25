import { describe, expect, it } from "vitest";

import de from "../../core/i18n/catalogs/de";
import {
  OCCUPATIONAL_FIELDS,
  evidenceKindsFor,
  uploadableEvidenceKinds,
  readOccupationalField,
  offersGitHub,
} from "./occupationalFields";

/** A catalogue key such as `credentials.masterCraftsmanCertificate`, resolved against the German catalogue. */
function catalogHas(key: string): boolean {
  return key
    .split(".")
    .reduce<unknown>(
      (value, part) =>
        typeof value === "object" && value !== null
          ? (value as Record<string, unknown>)[part]
          : undefined,
      de,
    ) !== undefined;
}

describe("occupational fields and their evidence (ADR-0039)", () => {
  it("carries exactly the server's eleven labels", () => {
    // A set, not a containment: a twelfth field should stand out.
    expect([...OCCUPATIONAL_FIELDS]).toEqual([
      "handwerk",
      "industrie_technik",
      "bau",
      "gesundheit_pflege",
      "logistik_verkehr",
      "gastronomie_hotel",
      "handel_verkauf",
      "buero_verwaltung",
      "it_software",
      "bildung_soziales",
      "sonstiges",
    ]);
  });

  it("reads an unknown label as \"none\" instead of failing", () => {
    expect(readOccupationalField("metallbauer")).toBeNull();
    expect(readOccupationalField(null)).toBeNull();
    expect(readOccupationalField("handwerk")).toBe("handwerk");
  });

  it("offers GitHub for it_software and without a field, otherwise not", () => {
    expect(offersGitHub(null)).toBe(true);
    expect(offersGitHub("it_software")).toBe(true);
    expect(offersGitHub("handwerk")).toBe(false);
    expect(offersGitHub("gesundheit_pflege")).toBe(false);
  });

  it("gives every field the common evidence", () => {
    // The field's own evidence comes on top, it replaces nothing.
    for (const field of OCCUPATIONAL_FIELDS) {
      const keys = evidenceKindsFor(field).map((kind) => kind.labelKey);

      expect(keys, field).toContain("credentials.employmentReference");
      expect(keys, field).toContain("credentials.certificate");
      expect(keys, field).toContain("credentials.reference");
    }
  });

  it("names each piece of evidence only once", () => {
    for (const field of OCCUPATIONAL_FIELDS) {
      const keys = evidenceKindsFor(field).map((kind) => kind.labelKey);

      expect(new Set(keys).size, field).toBe(keys.length);
    }
  });

  it("offers only the common evidence without an occupational field", () => {
    expect(evidenceKindsFor(null).map((kind) => kind.labelKey)).toEqual([
      "credentials.employmentReference",
      "credentials.certificate",
      "credentials.reference",
    ]);
  });

  it("lets police clearance and health certificate be named, never uploaded", () => {
    // Nameable, because employers ask for them — and never uploadable.
    expect(
      evidenceKindsFor("gesundheit_pflege").map((kind) => kind.labelKey),
    ).toContain("credentials.policeClearance");
    expect(
      uploadableEvidenceKinds("gesundheit_pflege").map((kind) => kind.labelKey),
    ).not.toContain("credentials.policeClearance");

    expect(
      evidenceKindsFor("gastronomie_hotel").map((kind) => kind.labelKey),
    ).toContain("credentials.healthCertificate");
    expect(
      uploadableEvidenceKinds("gastronomie_hotel").map((kind) => kind.labelKey),
    ).not.toContain("credentials.healthCertificate");
  });

  it("has a catalogue text for every piece of evidence", () => {
    // Otherwise a new piece of evidence would show its key on the page.
    for (const field of [...OCCUPATIONAL_FIELDS, null]) {
      for (const kind of evidenceKindsFor(field)) {
        expect(catalogHas(kind.labelKey), kind.labelKey).toBe(true);
      }
    }
  });

  it("has a catalogue name for every occupational field", () => {
    for (const field of OCCUPATIONAL_FIELDS) {
      expect(catalogHas(`occupation.field.${field}`), field).toBe(true);
    }
  });
});
