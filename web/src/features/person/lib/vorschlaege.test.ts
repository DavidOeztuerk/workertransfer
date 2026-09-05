import { describe, expect, it } from "vitest";

import type { Repository } from "../api/github";
import {
  ausArbeiten,
  ausRepositories,
  ausStationen,
  vorschlaegeAus,
} from "./vorschlaege";

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

describe("vorschlaegeAus", () => {
  it("nimmt Topics und Sprachen eines Repositories zusammen", () => {
    const gefunden = vorschlaegeAus(
      ausRepositories([repo("dienst", ["kubernetes"], ["Go", "Dockerfile"])]),
      []
    );

    expect(gefunden).toEqual(
      expect.arrayContaining(["kubernetes", "Go", "Dockerfile"])
    );
  });

  /**
   * Was die Person schon gesagt hat, wird ihr nicht noch einmal angeboten —
   * auch nicht in anderer Schreibweise.
   */
  it("schlägt nichts vor, was schon im Profil steht", () => {
    const gefunden = vorschlaegeAus(
      ausRepositories([repo("dienst", ["typescript", "react"])]),
      ["TypeScript"]
    );

    expect(gefunden).toEqual(["react"]);
  });

  /**
   * Die Reihenfolge ordnet WÖRTER, nie Menschen: was in mehreren Quellen
   * vorkommt, steht oben. Eine Zahl daraus wird nirgends gezeigt.
   */
  it("stellt nach vorn, was in mehreren Quellen vorkommt", () => {
    const gefunden = vorschlaegeAus(
      ausRepositories([
        repo("eins", ["go"]),
        repo("zwei", ["go"]),
        repo("drei", ["cobol"]),
      ]),
      []
    );

    expect(gefunden[0]).toBe("go");
  });

  it("zählt ein Wort je Quelle einmal, auch wenn es doppelt dasteht", () => {
    const gefunden = vorschlaegeAus(
      ausRepositories([
        repo("eins", ["Go"], ["Go"]),
        repo("zwei", ["rust"]),
        repo("drei", ["rust"]),
      ]),
      []
    );

    // „Go" steht in EINER Quelle, „rust" in zweien — also rust zuerst.
    expect(gefunden[0]).toBe("rust");
  });

  /**
   * Die zweite Quelle: die Technologien einer Lebenslauf-Station.
   *
   * Sie stehen gleichberechtigt neben den Repositories — beides sind Wörter,
   * die ein Mensch selbst hingeschrieben hat, und beides wird erst durch einen
   * Klick ins Profil zu einer Aussage über ihn.
   */
  it("nimmt Stationen und Repositories zusammen", () => {
    const gefunden = vorschlaegeAus(
      [
        ...ausRepositories([repo("dienst", ["Go"])]),
        ...ausStationen([
          { technologies: ["Kubernetes", "Terraform"] },
          { technologies: ["Kubernetes"] },
        ]),
      ],
      []
    );

    expect(gefunden).toEqual(
      expect.arrayContaining(["Go", "Kubernetes", "Terraform"])
    );
  });

  /**
   * <strong>Selbst Getipptes steht vorn — und das ist gemessen.</strong>
   *
   * Ohne diese Stufe ordnete allein die Häufigkeit. An einem echten Konto mit
   * hundert Repositories drückte das die drei Technologien aus dem eigenen
   * Lebenslauf komplett aus den obersten vierundzwanzig: „Kafka" stand in zwei
   * Stationen, „nodejs" in Dutzenden Repositories.
   *
   * Ein Wort, das jemand über SEINE ARBEIT geschrieben hat, wiegt schwerer als
   * eins, das an einem Artefakt gefunden wurde — dieselbe Rangfolge wie
   * „Genannt vor Belegt".
   */
  it("stellt selbst Getipptes vor häufig Gefundenes", () => {
    const viele = Array.from({ length: 30 }, (_, i) =>
      repo(`r${i}`, ["nodejs", `thema${i}`])
    );

    const gefunden = vorschlaegeAus(
      [
        ...ausRepositories(viele),
        ...ausStationen([{ technologies: ["Kafka"] }]),
      ],
      []
    );

    // „nodejs" steht in dreissig Quellen, „Kafka" in einer — und trotzdem vorn.
    expect(gefunden[0]).toBe("Kafka");
    expect(gefunden).toContain("nodejs");
  });

  /**
   * Einmal selbst getippt bleibt selbst getippt.
   *
   * Dass dasselbe Wort auch an einem Repository klebt, macht die eigene Angabe
   * nicht schwächer.
   */
  it("verliert die eigene Angabe nicht, wenn sie auch am Repository steht", () => {
    const viele = Array.from({ length: 10 }, (_, i) => repo(`r${i}`, ["nodejs"]));

    const gefunden = vorschlaegeAus(
      [
        ...ausRepositories([...viele, repo("extra", ["Kafka"])]),
        ...ausStationen([{ technologies: ["Kafka"] }]),
      ],
      []
    );

    expect(gefunden[0]).toBe("Kafka");
  });

  it("bietet höchstens vierundzwanzig an", () => {
    const viele = Array.from({ length: 60 }, (_, i) =>
      repo(`r${i}`, [`thema${i}`])
    );

    expect(vorschlaegeAus(ausRepositories(viele), [])).toHaveLength(24);
  });

  it("kommt mit leeren Quellen zurecht", () => {
    expect(vorschlaegeAus([], [])).toEqual([]);
    expect(vorschlaegeAus(ausRepositories([repo("still")]), ["Go"])).toEqual([]);
  });

  /** Arbeiten zählen wie Stationen: selbst hingeschrieben. */
  it("stellt auch die Technologien einer Arbeit vor Gefundenes", () => {
    const viele = Array.from({ length: 20 }, (_, i) => repo(`r${i}`, ["nodejs"]));

    const gefunden = vorschlaegeAus(
      [...ausRepositories(viele), ...ausArbeiten([{ technologies: ["Grafana"] }])],
      []
    );

    expect(gefunden[0]).toBe("Grafana");
  });
});
