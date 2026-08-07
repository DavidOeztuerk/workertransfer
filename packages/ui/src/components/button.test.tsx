import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Button } from "./button";

describe("Button", () => {
  it("renders its children", () => {
    render(<Button>Anmelden</Button>);
    expect(screen.getByRole("button", { name: "Anmelden" })).toBeInTheDocument();
  });

  it("defaults to the primary variant class", () => {
    render(<Button>Start</Button>);
    const button = screen.getByRole("button");
    expect(button).toHaveClass("wt-button", "wt-button--primary");
  });

  it.each(["primary", "secondary", "quiet"] as const)("applies the %s variant", (variant) => {
    render(<Button variant={variant}>x</Button>);
    expect(screen.getByRole("button")).toHaveClass(`wt-button--${variant}`);
  });

  it("merges a caller className instead of replacing the base classes", () => {
    render(<Button className="extra">x</Button>);
    expect(screen.getByRole("button")).toHaveClass("wt-button", "wt-button--primary", "extra");
  });

  it('defaults to type="button" so it never submits a form by accident', () => {
    render(<Button>x</Button>);
    expect(screen.getByRole("button")).toHaveAttribute("type", "button");
  });

  it("lets a caller override the type", () => {
    // The login form relies on this: props are spread after the default, so
    // <Button type="submit"> really submits. Reordering the spread would
    // silently break form submission.
    render(<Button type="submit">x</Button>);
    expect(screen.getByRole("button")).toHaveAttribute("type", "submit");
  });

  it("forwards arbitrary button attributes", () => {
    render(<Button disabled aria-label="Speichern">x</Button>);
    const button = screen.getByRole("button", { name: "Speichern" });
    expect(button).toBeDisabled();
  });
});
