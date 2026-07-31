import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Card } from "./card";

describe("Card", () => {
  it("renders its children inside a section element", () => {
    const { container } = render(<Card>Inhalt</Card>);
    expect(screen.getByText("Inhalt")).toBeInTheDocument();
    expect(container.querySelector("section")).not.toBeNull();
  });

  it("merges a caller className instead of replacing the base class", () => {
    const { container } = render(<Card className="principle">x</Card>);
    expect(container.querySelector("section")).toHaveClass("wt-card", "principle");
  });

  it("forwards arbitrary section attributes", () => {
    render(
      <Card aria-labelledby="t">
        <h2 id="t">Titel</h2>
      </Card>
    );
    expect(screen.getByRole("region", { name: "Titel" })).toBeInTheDocument();
  });
});
