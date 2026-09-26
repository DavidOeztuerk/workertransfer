import { describe, expect, it } from "vitest";

import { LANGUAGES } from "../store/preferencesSlice";
import de from "./catalogs/de";
import en from "./catalogs/en";
import fr from "./catalogs/fr";

/** Every key as a flat path, so that a comparison says something. */
function paths(value: unknown, prefix = ""): string[] {
  if (typeof value !== "object" || value === null) {
    return [prefix];
  }

  return Object.entries(value).flatMap(([key, below]) =>
    paths(below, prefix === "" ? key : `${prefix}.${key}`)
  );
}

const catalogs = { de, en, fr };

describe("the catalogues", () => {
  // The type already pins the keys — but only while compiling. Whoever loads
  // the catalogues as JSON once, or loosens the type, loses that without a
  // word. This test holds it independently.
  it("have the same keys in every language", () => {
    const source = paths(de).sort();

    for (const language of LANGUAGES) {
      expect(paths(catalogs[language]).sort(), `catalogue ${language}`).toEqual(source);
    }
  });

  // A copied line nobody translated looks like a missing translation in the
  // UI — except that it stands out nowhere.
  //
  // The exemptions are per LANGUAGE, not per key: "Link" is the same word in
  // English, in French it is "Lien" — an exemption for the key would let the
  // French line slip through silently.
  it("actually translate instead of copying German", () => {
    const allowed = new Set([
      // A unit is not a translation. "km" is km in all three languages, and an
      // invented difference would be wrong.
      "en:jobs.kilometers",
      "fr:jobs.kilometers",
      // "KB" is KB in English too; French writes "Ko" and stands on no
      // exemption.
      "en:resume.sizeKb",
      // Language names stand in their own language, in every catalogue.
      "en:language.de",
      "en:language.en",
      "en:language.fr",
      "fr:language.de",
      "fr:language.en",
      "fr:language.fr",
      // The German wording IS English here — a slogan, not an oversight.
      "en:home.principle3Title",
      // The same word in both languages. Every line is an individual case and
      // no licence — French stands on none of them.
      "en:portfolio.fieldLink",
      // "Link" is the same in English, and an invented difference would be
      // wrong. French does NOT stand beside it: there it is "Lien".
      "en:assessments.solutionLink",
      "en:market.status",
      "en:consents.areaGithub",
      "en:job.remoteHybrid",
      // "remote" and "hybrid" are the same words in English — they were
      // borrowed there, not translated here. French does NOT stand beside
      // them: there they are "à distance" and "hybride", and an exemption per
      // key would let exactly that slip through silently.
      "en:scout.attendance_remote",
      "en:scout.attendance_hybrid",
      "en:jobs.website",
      "en:companyProfile.website",
      "en:transfers.start",
      "en:companyTransfers.start",
      "en:companyTransfers.title",
      "en:myData.sectionTransfers",
      "en:team.roleAdmin",
      // "System" is the same word in English. French does not stand beside
      // it — there it is "Systeme", and an exemption per key would let exactly
      // that line slip through silently.
      "en:appearance.system",
      "en:header.github",
      "en:header.team",
      "en:header.navigation",
      "fr:header.navigation",
      "fr:consents.areaProfile",
      "fr:consents.areaGithub",
      "fr:applications.partProfile",
      "fr:header.profile",
      "fr:myData.sectionProfile",
      "fr:header.github",
      // Product names, the same in every language.
      "en:scout.skillsExample",
      "fr:scout.skillsExample",
      "en:jobs.skillsExample",
      "fr:jobs.skillsExample",
      // A copyright notice is the same in every language.
      "en:footer.copyright",
      // A company name is the same in every language.
      "en:settings.providerAnthropic",
      "fr:settings.providerAnthropic",
      // "Adresse" is the same word in French.
      "fr:settings.address",
      "fr:footer.copyright",
      "fr:scout.skillsExample",
    ]);

    for (const language of ["en", "fr"] as const) {
      const same = paths(de).filter(
        (path) =>
          !allowed.has(`${language}:${path}`) &&
          read(de, path) === read(catalogs[language], path)
      );

      expect(same, `untranslated in ${language}`).toEqual([]);
    }
  });

  it("leave no text empty", () => {
    for (const language of LANGUAGES) {
      for (const path of paths(catalogs[language])) {
        expect(read(catalogs[language], path).trim(), `${language}: ${path}`).not.toBe("");
      }
    }
  });
});

function read(catalog: unknown, path: string): string {
  return path.split(".").reduce<unknown>(
    (value, part) => (value as Record<string, unknown>)[part],
    catalog
  ) as string;
}
