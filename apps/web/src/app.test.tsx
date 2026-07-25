import { render, screen } from "@testing-library/react";

import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";

describe("HomeRoute", () => {
  it("states the consent-first product promise", () => {
    render(<HomeRoute />);

    expect(
      screen.getByRole("heading", {
        name: "Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen."
      })
    ).toBeInTheDocument();
    expect(screen.getByText("Du entscheidest")).toBeInTheDocument();
  });
});

describe("LoginRoute", () => {
  it("renders the German login heading", () => {
    render(<LoginRoute />);
    expect(screen.getByRole("heading", { name: "Anmelden" })).toBeInTheDocument();
  });
});
