import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Button } from "./button";

describe("Button", () => {
  it("renders its label", () => {
    render(<Button>Anmelden</Button>);

    expect(screen.getByRole("button", { name: "Anmelden" })).toBeInTheDocument();
  });

  it("calls its handler", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Anmelden</Button>);

    await user.click(screen.getByRole("button"));

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("stays silent while disabled", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(
      <Button disabled onClick={onClick}>
        Anmelden
      </Button>
    );

    await user.click(screen.getByRole("button"));

    expect(onClick).not.toHaveBeenCalled();
  });

  it("defaults to type=button so it never submits a form by accident", () => {
    render(<Button>Anmelden</Button>);

    expect(screen.getByRole("button")).toHaveAttribute("type", "button");
  });

  // Mit `href` wird ein LINK daraus, kein Knopf. Ein <button> in ein <a> zu
  // wickeln wäre ungültiges HTML, und ein <button> mit onClick={navigate}
  // nähme dem Nutzer alles, was ein Link kann: Mittelklick, neuer Tab,
  // Adresse kopieren, Vorschau in der Statusleiste.
  it("becomes a link when it points somewhere", () => {
    render(<Button href="/register">Als Arbeitnehmer starten</Button>);

    const link = screen.getByRole("link", { name: "Als Arbeitnehmer starten" });
    expect(link).toHaveAttribute("href", "/register");
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("keeps its look as a link", () => {
    render(
      <Button href="/register" variant="secondary">
        Als Unternehmen entdecken
      </Button>
    );

    // Ausnahme von der Regel „kein Klassentest": dass ein Link AUSSIEHT wie ein
    // Knopf, ist hier die ganze Zusage, und sie ist nicht anders prüfbar.
    expect(screen.getByRole("link")).toHaveClass("wt-button", "wt-button--secondary");
  });
});
