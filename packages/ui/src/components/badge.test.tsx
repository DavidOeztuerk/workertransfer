import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Badge } from "./badge";

describe("Badge", () => {
  // Eine nackte Zahl ist für einen Vorleser bedeutungslos: man hört „3" und
  // weiß nicht, drei von was. Das Label liefert das Substantiv.
  it("says what it counts, not just how many", () => {
    render(<Badge count={3} label="3 offene Anfragen" />);

    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByLabelText("3 offene Anfragen")).toBeInTheDocument();
  });

  // Eine Null ist keine Nachricht, sondern ein Aufmerksamkeitsanspruch ohne
  // Anlass.
  it("shows nothing at zero", () => {
    const { container } = render(<Badge count={0} label="0 offene Anfragen" />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows nothing for a negative count", () => {
    const { container } = render(<Badge count={-1} label="kaputt" />);

    expect(container).toBeEmptyDOMElement();
  });
});
