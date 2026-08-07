import { screen } from "@testing-library/react";

import { renderWithProviders } from "./test/render";
import { HomeRoute } from "./routes/home";
import { LoginRoute } from "./routes/login";

describe("HomeRoute", () => {
  it("states the consent-first product promise", () => {
    renderWithProviders(<HomeRoute />);

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
    renderWithProviders(<LoginRoute />);
    expect(screen.getByRole("heading", { name: "Anmelden" })).toBeInTheDocument();
  });
});

describe("Auth-Layout", () => {
  it("puts the form before the brand panel in the document", () => {
    // Bedienreihenfolge vor Optik: Tastatur und Screenreader sollen bei der
    // Aufgabe anfangen, nicht bei einem Werbesatz. Visuell steht das Panel
    // trotzdem links (Rasterspalten in styles.css).
    const { container } = renderWithProviders(<LoginRoute />);

    const main = container.querySelector("main.auth");
    const children = Array.from(main?.children ?? []);
    const panelIndex = children.findIndex((el) => el.classList.contains("auth__panel"));
    const asideIndex = children.findIndex((el) => el.classList.contains("auth__aside"));

    expect(panelIndex).toBeGreaterThanOrEqual(0);
    expect(asideIndex).toBeGreaterThan(panelIndex);
  });

  it("keeps the brand panel readable by assistive tech", () => {
    // Kein aria-hidden: der Satz ist Inhalt, kein Zierrat.
    renderWithProviders(<LoginRoute />);

    expect(screen.getByText("Wechseln ist eine Entscheidung, kein Zufall.")).toBeInTheDocument();
  });

  it("gives the credential fields autocomplete hints", () => {
    // Ohne die kann kein Passwortmanager füllen — der Browser mahnt es selbst an.
    renderWithProviders(<LoginRoute />);

    expect(screen.getByLabelText("E-Mail")).toHaveAttribute("autocomplete", "username");
    expect(screen.getByLabelText("Passwort")).toHaveAttribute("autocomplete", "current-password");
  });
});
