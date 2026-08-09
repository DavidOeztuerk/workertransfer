import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Alert } from "./alert";

describe("Alert", () => {
  // Der Unterschied zwischen den Varianten ist VERHALTEN, nicht Farbe:
  // role="alert" unterbricht den Screenreader, role="status" wartet. Eine
  // Fehlermeldung, die wartet, kommt zu spät; eine Bestätigung, die
  // unterbricht, ist Lärm.
  it("interrupts for an error", () => {
    render(<Alert>Anmeldung fehlgeschlagen</Alert>);

    expect(screen.getByRole("alert")).toHaveTextContent("Anmeldung fehlgeschlagen");
  });

  it("waits its turn for a notice", () => {
    render(<Alert variant="notice">Freigabe erteilt</Alert>);

    expect(screen.getByRole("status")).toHaveTextContent("Freigabe erteilt");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  // Der Standard trifft den häufigen Fall: der Bestand setzt fast überall
  // role="alert". Wäre "notice" der Standard, würde jede vergessene Variante
  // eine Fehlermeldung leise entschärfen.
  it("defaults to the interrupting variant", () => {
    render(<Alert>Etwas ist schiefgegangen</Alert>);

    expect(screen.getByRole("alert")).toBeInTheDocument();
  });
});
