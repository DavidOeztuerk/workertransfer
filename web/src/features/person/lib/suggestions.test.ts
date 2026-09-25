import { describe, expect, it } from "vitest";

import type { Repository } from "../api/github";
import {
  fromPortfolioItems,
  fromPositions,
  fromRepositories,
  suggestionsFrom,
} from "./suggestions";

function repo(
  name: string,
  topics: string[] = [],
  languages: string[] = []
): Repository {
  return {
    name,
    description: "",
    language: languages[0] ?? null,
    stars: 0,
    url: `https://github.com/anna/${name}`,
    pushed_at: null,
    languages,
    topics,
  };
}

describe("suggestionsFrom", () => {
  it("takes a repository's topics and languages together", () => {
    const found = suggestionsFrom(
      fromRepositories([repo("service", ["kubernetes"], ["Go", "Dockerfile"])]),
      []
    );

    expect(found).toEqual(
      expect.arrayContaining(["kubernetes", "Go", "Dockerfile"])
    );
  });

  /**
   * What the person has already said is not offered to them again — not even
   * in another spelling.
   */
  it("suggests nothing that already stands in the profile", () => {
    const found = suggestionsFrom(
      fromRepositories([repo("service", ["typescript", "react"])]),
      ["TypeScript"]
    );

    expect(found).toEqual(["react"]);
  });

  /**
   * The order ranks WORDS, never people: what occurs in several sources stands
   * on top. No number from it is shown anywhere.
   */
  it("puts first what occurs in several sources", () => {
    const found = suggestionsFrom(
      fromRepositories([
        repo("one", ["go"]),
        repo("two", ["go"]),
        repo("three", ["cobol"]),
      ]),
      []
    );

    expect(found[0]).toBe("go");
  });

  it("counts a word once per source, even when it stands there twice", () => {
    const found = suggestionsFrom(
      fromRepositories([
        repo("one", ["Go"], ["Go"]),
        repo("two", ["rust"]),
        repo("three", ["rust"]),
      ]),
      []
    );

    // "Go" stands in ONE source, "rust" in two — so rust first.
    expect(found[0]).toBe("rust");
  });

  /**
   * The second source: the technologies of a CV position.
   *
   * They stand on an equal footing beside the repositories — both are words a
   * human wrote themselves, and both become a statement about them only
   * through a click into the profile.
   */
  it("takes positions and repositories together", () => {
    const found = suggestionsFrom(
      [
        ...fromRepositories([repo("service", ["Go"])]),
        ...fromPositions([
          { technologies: ["Kubernetes", "Terraform"] },
          { technologies: ["Kubernetes"] },
        ]),
      ],
      []
    );

    expect(found).toEqual(
      expect.arrayContaining(["Go", "Kubernetes", "Terraform"])
    );
  });

  /**
   * <strong>Self-typed comes first — and that was measured.</strong>
   *
   * Without this step frequency alone decided the order. On a real account
   * with a hundred repositories that pushed the three technologies from one's
   * own CV completely out of the top twenty-four: "Kafka" stood in two
   * positions, "nodejs" in dozens of repositories.
   *
   * A word somebody wrote about THEIR WORK weighs more than one found on an
   * artefact — the same precedence as "named before evidenced".
   */
  it("puts self-typed words before frequently found ones", () => {
    const many = Array.from({ length: 30 }, (_, i) =>
      repo(`r${i}`, ["nodejs", `topic${i}`])
    );

    const found = suggestionsFrom(
      [
        ...fromRepositories(many),
        ...fromPositions([{ technologies: ["Kafka"] }]),
      ],
      []
    );

    // "nodejs" stands in thirty sources, "Kafka" in one — and still first.
    expect(found[0]).toBe("Kafka");
    expect(found).toContain("nodejs");
  });

  /**
   * Once self-typed stays self-typed.
   *
   * That the same word also sticks to a repository does not make one's own
   * statement weaker.
   */
  it("does not lose one's own statement when it also stands on a repository", () => {
    const many = Array.from({ length: 10 }, (_, i) => repo(`r${i}`, ["nodejs"]));

    const found = suggestionsFrom(
      [
        ...fromRepositories([...many, repo("extra", ["Kafka"])]),
        ...fromPositions([{ technologies: ["Kafka"] }]),
      ],
      []
    );

    expect(found[0]).toBe("Kafka");
  });

  it("offers twenty-four at most", () => {
    const many = Array.from({ length: 60 }, (_, i) =>
      repo(`r${i}`, [`topic${i}`])
    );

    expect(suggestionsFrom(fromRepositories(many), [])).toHaveLength(24);
  });

  it("copes with empty sources", () => {
    expect(suggestionsFrom([], [])).toEqual([]);
    expect(suggestionsFrom(fromRepositories([repo("quiet")]), ["Go"])).toEqual([]);
  });

  /** Portfolio items count like positions: written by oneself. */
  it("also puts a portfolio item's technologies before found ones", () => {
    const many = Array.from({ length: 20 }, (_, i) => repo(`r${i}`, ["nodejs"]));

    const found = suggestionsFrom(
      [...fromRepositories(many), ...fromPortfolioItems([{ technologies: ["Grafana"] }])],
      []
    );

    expect(found[0]).toBe("Grafana");
  });
});
