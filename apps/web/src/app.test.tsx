import { render, screen } from "@testing-library/react";

import { App } from "./app";

describe("App", () => {
  it("states the consent-first product promise", () => {
    render(<App />);

    expect(
      screen.getByRole("heading", {
        name: "Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen."
      })
    ).toBeInTheDocument();
    expect(screen.getByText("Du entscheidest")).toBeInTheDocument();
  });
});
