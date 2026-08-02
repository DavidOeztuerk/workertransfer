import { describe, expect, it } from "vitest";

import { NO_FILTERS, candidateQuery } from "./client";

describe("candidateQuery", () => {
  it("is empty when nothing is asked for", () => {
    expect(candidateQuery(undefined, NO_FILTERS)).toBe("");
  });

  it("repeats the skill parameter — the server joins them with AND", () => {
    const query = candidateQuery(undefined, { ...NO_FILTERS, skills: ["Python", "Kubernetes"] });

    expect(query).toBe("?skill=Python&skill=Kubernetes");
  });

  it("drops blank skills instead of sending empty filters", () => {
    const query = candidateQuery(undefined, { ...NO_FILTERS, skills: ["  ", "Go", ""] });

    expect(query).toBe("?skill=Go");
  });

  it("trims what a person typed", () => {
    const query = candidateQuery(undefined, { ...NO_FILTERS, skills: [" Rust "], location: " Berlin " });

    expect(query).toBe("?skill=Rust&location=Berlin");
  });

  it("sends remote only when it is on", () => {
    // `remote=false` wäre kein Filter, sondern Rauschen in der URL — und auf
    // dem Server ein Ausschluss von Leuten, die nur nichts angekreuzt haben.
    expect(candidateQuery(undefined, { ...NO_FILTERS, remoteOnly: true })).toBe("?remote=true");
    expect(candidateQuery(undefined, { ...NO_FILTERS, remoteOnly: false })).toBe("");
  });

  it("carries the cursor alongside the filters, never inside them", () => {
    const query = candidateQuery("abc123", { ...NO_FILTERS, skills: ["Go"] });

    expect(query).toBe("?cursor=abc123&skill=Go");
  });

  it("escapes what a person typed", () => {
    const query = candidateQuery(undefined, { ...NO_FILTERS, location: "Frankfurt & Umgebung" });

    expect(query).toContain("Frankfurt+%26+Umgebung");
  });
});
