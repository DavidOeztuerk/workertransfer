import { describe, expect, it } from "vitest";

import de from "../../../core/i18n/catalogs/de";
import { parseCapability } from "./consent";

/**
 * Every capability the backend grants must get a name here.
 *
 * The occasion: `github.visibility:public` was missing from the table, and the
 * releases page showed the raw string for it. That is no cosmetic flaw — on the
 * page where somebody withdraws their releases, an unreadable entry is exactly
 * the one that gets left standing.
 *
 * The list is written out and not derived from the backend: a new capability
 * should turn this test red so that somebody chooses a word for it, instead of
 * it being noticed the first time it is shown.
 *
 * What is checked is the KEY and that it stands in the catalogue. Only both
 * together hold: i18next hands an unknown key back unchanged, so a typo would
 * stand raw on exactly the page this is about.
 */
const ALL_AREAS = [
  ["profile", "areaProfile"],
  ["resume", "areaResume"],
  ["portfolio", "areaPortfolio"],
  ["market", "areaMarket"],
  ["github", "areaGithub"],
] as const;

describe("parseCapability", () => {
  it.each(ALL_AREAS)("names a catalogue key for %s", (raw, name) => {
    const key = `consents.${name}`;
    expect(parseCapability(`${raw}.visibility:public`).area).toBe(key);
    expect(de.consents).toHaveProperty(name);
  });

  it("names the tenant when the release applies to one company", () => {
    const id = "7d9d3890-eccb-47f7-b793-514ce6283fdf";

    expect(parseCapability(`market.visibility:tenant:${id}`)).toEqual({
      area: "consents.areaMarket",
      tenantId: id,
      public: false,
    });
  });

  it("recognises a release to everyone", () => {
    expect(parseCapability("profile.visibility:public")).toEqual({
      area: "consents.areaProfile",
      tenantId: null,
      public: true,
    });
  });

  /**
   * An unknown shape is shown, not swallowed. Hiding a release because its
   * format was not recognised would be the worst conceivable defect on this
   * page — it could then not be withdrawn either.
   */
  it("returns null for an unknown shape instead of guessing", () => {
    expect(parseCapability("something.else.entirely").area).toBeNull();
  });
});
