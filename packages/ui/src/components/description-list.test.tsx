import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { DescriptionList } from "./description-list";

describe("DescriptionList", () => {
  it("shows every term and its description", () => {
    render(
      <DescriptionList
        items={[
          { term: "Gehalt", description: "58.000 €" },
          { term: "Eintritt", description: "1. Oktober" },
        ]}
      />
    );

    expect(screen.getByText("Gehalt")).toBeInTheDocument();
    expect(screen.getByText("58.000 €")).toBeInTheDocument();
    expect(screen.getByText("Eintritt")).toBeInTheDocument();
    expect(screen.getByText("1. Oktober")).toBeInTheDocument();
  });

  // Hier wird ausnahmsweise die Struktur geprüft und keine Rolle: <dl>/<dt>/<dd>
  // haben in ARIA keine abfragbare Rolle, und die ZUORDNUNG ist genau das, was
  // dieses Bauteil leistet. Steht die Beschreibung nicht direkt hinter ihrem
  // Begriff, ist die Liste falsch, auch wenn beide Texte da sind.
  it("puts each description directly after its own term", () => {
    render(
      <DescriptionList
        items={[
          { term: "Gehalt", description: "58.000 €" },
          { term: "Eintritt", description: "1. Oktober" },
        ]}
      />
    );

    const term = screen.getByText("Gehalt");
    expect(term.tagName).toBe("DT");
    expect(term.nextElementSibling?.tagName).toBe("DD");
    expect(term.nextElementSibling).toHaveTextContent("58.000 €");
  });
});
