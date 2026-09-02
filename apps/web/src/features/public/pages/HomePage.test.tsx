import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../test/render";
import { HomePage } from "./HomePage";

describe("HomePage", () => {
  it("trägt genau eine Überschrift erster Ordnung", () => {
    renderMitStore(<HomePage />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  // Die beiden auffälligsten Elemente der Seite waren im alten Stand tote
  // Knöpfe: <Button> ohne onClick und ohne href. Ein Klick tat nichts.
  //
  // Und der zweite trägt die ABSICHT mit: ohne `?as=company` landet jemand, der
  // „Als Unternehmen entdecken" klickt, im Personenformular — und merkt es erst
  // nach der Bestätigungsmail, wenn kein Unternehmen da ist.
  it("bietet zwei Wege hinein, und beide tragen ihre Absicht", () => {
    renderMitStore(<HomePage />);

    expect(screen.getByRole("link", { name: "Als Arbeitnehmer starten" })).toHaveAttribute(
      "href",
      "/register"
    );
    expect(screen.getByRole("link", { name: "Als Unternehmen entdecken" })).toHaveAttribute(
      "href",
      "/register?as=company"
    );
  });

  it("nennt seine drei Grundlagen", () => {
    renderMitStore(<HomePage />);

    expect(screen.getByRole("heading", { name: "Du entscheidest" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Echte Nachweise" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Human in control" })).toBeInTheDocument();
  });

  // Die Abschnitte tragen Namen, damit ein Screenreader sie in der
  // Regionenliste unterscheiden kann. Drei namenlose „region"-Einträge sind
  // keine Navigation.
  it("benennt jeden Abschnitt, den es anbietet", () => {
    renderMitStore(<HomePage />);

    for (const name of [
      "Neue Arbeit soll sich wie eine selbstbestimmte Entscheidung anfühlen.",
      "Vertrauen ist kein Feature. Es ist die Architektur.",
      "Eine Plattform, die mit einer klaren ersten Grundlage wächst.",
    ]) {
      expect(screen.getByRole("region", { name })).toBeInTheDocument();
    }
  });

  /**
   * Die Trennung, wegen der es `/overview` überhaupt gibt.
   *
   * Vorher bediente `/` beides. Die Übersicht war damit nicht verlinkbar, und
   * ein Screenshot von `/` zeigte je nach Sitzung etwas anderes.
   */
  it("schickt eine angemeldete Besucherin auf die Übersicht", () => {
    renderMitStore(<HomePage />, { auth: { status: "authenticated" } });

    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
  });

  // Solange niemand weiss, wer da ist, wird NICHTS gezeichnet: der Sprung
  // Werbung → Übersicht sieht nach einem Fehler aus.
  it("zeichnet nichts, solange die Sitzung unbekannt ist", () => {
    renderMitStore(<HomePage />, { auth: { status: "unknown" } });

    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(screen.queryByRole("link", { name: "Als Arbeitnehmer starten" })).toBeNull();
  });
});
