import { describe, expect, it } from "vitest";

import { parseCapability } from "./client";

describe("parseCapability", () => {
  it("splits a receiver-bound capability into area and company", () => {
    const parsed = parseCapability("resume.visibility:tenant:22222222-2222-2222-2222-222222222222");

    expect(parsed).toEqual({
      area: "Lebenslauf",
      tenantId: "22222222-2222-2222-2222-222222222222",
      public: false,
    });
  });

  it("recognises a public grant as covering every company", () => {
    expect(parseCapability("profile.visibility:public")).toEqual({
      area: "Profil",
      tenantId: null,
      public: true,
    });
  });

  it("knows all four areas that exist today", () => {
    const areas = ["profile", "resume", "portfolio", "market"].map(
      (area) => parseCapability(`${area}.visibility:public`).area
    );

    expect(areas).toEqual(["Profil", "Lebenslauf", "Arbeiten", "Marktstatus"]);
  });

  it("returns a null area for an unknown shape rather than guessing", () => {
    // Die Oberfläche zeigt sie dann roh — verschluckt wird nichts.
    expect(parseCapability("something.entirely:new").area).toBeNull();
    expect(parseCapability("").area).toBeNull();
  });

  it("returns a null area for an unknown area in a known shape", () => {
    // Ein neuer Bereich soll auftauchen, nicht verschwinden, bis jemand ihn
    // benennt.
    expect(parseCapability("contracts.visibility:public").area).toBeNull();
  });

  it("does not accept something that merely looks like a tenant id", () => {
    expect(parseCapability("resume.visibility:tenant:not-a-uuid").area).toBeNull();
  });
});
