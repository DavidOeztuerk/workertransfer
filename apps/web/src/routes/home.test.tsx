import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { HomeRoute } from "./home";

describe("HomeRoute", () => {
  it("carries exactly one first-level heading", () => {
    render(<HomeRoute />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  // Die beiden auffälligsten Elemente der Seite, die jeder zuerst sieht, waren
  // tote Knöpfe: <Button> ohne onClick und ohne href. Ein Klick tat nichts.
  //
  // Und der zweite trägt die ABSICHT mit: ohne `?as=company` landet jemand, der
  // „Als Unternehmen entdecken" klickt, im Personenformular — und merkt es erst
  // nach der Bestätigungsmail, wenn kein Unternehmen da ist.
  it("offers two ways in, and both carry their intent", () => {
    render(<HomeRoute />);

    expect(screen.getByRole("link", { name: "Als Arbeitnehmer starten" })).toHaveAttribute(
      "href",
      "/register"
    );
    expect(screen.getByRole("link", { name: "Als Unternehmen entdecken" })).toHaveAttribute(
      "href",
      "/register?as=company"
    );
  });

  it("names its three foundations", () => {
    render(<HomeRoute />);

    expect(screen.getByRole("heading", { name: "Du entscheidest" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Echte Nachweise" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Human in control" })).toBeInTheDocument();
  });

  // Die Abschnitte tragen Namen, damit ein Screenreader sie in der
  // Regionenliste unterscheiden kann. Drei namenlose „region"-Einträge sind
  // keine Navigation.
  it("names every section it offers", () => {
    render(<HomeRoute />);

    for (const name of [
      "Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen.",
      "Vertrauen ist kein Feature. Es ist die Architektur.",
      "Eine Plattform, die mit einer klaren ersten Grundlage wächst.",
    ]) {
      expect(screen.getByRole("region", { name })).toBeInTheDocument();
    }
  });
});
