import { describe, expect, it } from "vitest";

import { matchSkills } from "./match";

describe("matchSkills", () => {
  it("splits the job's list into what you have and what you don't", () => {
    const match = matchSkills(["Python", "Kubernetes", "Go"], ["Python", "Kubernetes", "Rust"]);

    expect(match.have).toEqual(["Python", "Kubernetes"]);
    expect(match.missing).toEqual(["Go"]);
  });

  it("compares without regard to case", () => {
    // Die Stelle schreibt „PostgreSQL", das Profil „postgresql". Wären das
    // zwei Dinge, fände der Abgleich fast nichts — und die Person sähe eine
    // Lücke, die es nicht gibt.
    const match = matchSkills(["PostgreSQL"], ["postgresql"]);

    expect(match.have).toEqual(["PostgreSQL"]);
    expect(match.missing).toEqual([]);
  });

  it("shows the job's spelling, not the profile's", () => {
    // Die Liste ist die der Ausschreibung; sie wird nur abgehakt. Sie mit den
    // Wörtern der Person zu überschreiben wäre eine stille Umdeutung dessen,
    // was das Unternehmen geschrieben hat.
    const match = matchSkills(["TypeScript"], ["typescript"]);

    expect(match.have).toEqual(["TypeScript"]);
  });

  it("keeps the order of the job's list", () => {
    const match = matchSkills(["A", "B", "C", "D"], ["D", "B"]);

    expect(match.have).toEqual(["B", "D"]);
    expect(match.missing).toEqual(["A", "C"]);
  });

  it("ignores surrounding space on both sides", () => {
    const match = matchSkills([" Go "], ["Go  "]);

    expect(match.missing).toEqual([]);
  });

  it("counts a skill once, however often the profile repeats it", () => {
    const match = matchSkills(["Go"], ["Go", "go", "GO"]);

    expect(match.have).toEqual(["Go"]);
  });

  it("finds nothing when the job names nothing", () => {
    const match = matchSkills([], ["Python"]);

    expect(match.have).toEqual([]);
    expect(match.missing).toEqual([]);
  });

  it("does not partially match — a skill is a whole thing", () => {
    // „Java" ist nicht „JavaScript". Ein Teilstring-Vergleich fände hier einen
    // Treffer, den niemand behauptet hat, und die Person ginge mit einer
    // falschen Auskunft in ein Gespräch.
    const match = matchSkills(["JavaScript"], ["Java"]);

    expect(match.missing).toEqual(["JavaScript"]);
  });
});
