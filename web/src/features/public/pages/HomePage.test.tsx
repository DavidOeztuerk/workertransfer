import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { renderWithStore } from "../test/render";
import { HomePage } from "./HomePage";

describe("HomePage", () => {
  it("carries exactly one first-level heading", () => {
    renderWithStore(<HomePage />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  // The two most striking elements of the page were dead buttons in the old
  // state: <Button> without onClick and without href. A click did nothing.
  //
  // And the second carries the INTENT along: without `?as=company` someone who
  // clicks "Als Unternehmen entdecken" lands in the person form — and notices
  // only after the confirmation mail, when no company is there.
  it("offers two ways in, and both carry their intent", () => {
    renderWithStore(<HomePage />);

    expect(
      screen.getByRole("link", { name: "Als Arbeitnehmer starten" }),
    ).toHaveAttribute("href", "/register");
    expect(
      screen.getByRole("link", { name: "Als Unternehmen entdecken" }),
    ).toHaveAttribute("href", "/register?as=company");
  });

  it("names its three principles", () => {
    renderWithStore(<HomePage />);

    expect(
      screen.getByRole("heading", { name: "Du entscheidest" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Echte Nachweise" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Human in control" }),
    ).toBeInTheDocument();
  });

  // The sections carry names so a screen reader can tell them apart in the
  // list of regions. Three nameless "region" entries are no navigation.
  it("names every section it offers", () => {
    renderWithStore(<HomePage />);

    for (const name of [
      "Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen.",
      "Worauf die Plattform aufbaut.",
      "Eine Plattform, die mit einer klaren ersten Grundlage wächst.",
    ]) {
      expect(screen.getByRole("region", { name })).toBeInTheDocument();
    }
  });

  /**
   * The separation `/overview` exists for at all.
   *
   * Before, `/` served both. The overview was therefore not linkable, and a
   * screenshot of `/` showed something different depending on the session.
   */
  it("sends a signed-in visitor to the overview", () => {
    renderWithStore(<HomePage />, { auth: { status: "authenticated" } });

    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
  });

  // As long as nobody knows who is there, NOTHING is drawn: the jump
  // advertising → overview looks like a defect.
  it("draws nothing while the session is unknown", () => {
    renderWithStore(<HomePage />, { auth: { status: "unknown" } });

    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(
      screen.queryByRole("link", { name: "Als Arbeitnehmer starten" }),
    ).toBeNull();
  });
});
