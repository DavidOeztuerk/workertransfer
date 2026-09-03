import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import { SettingsMenu } from "./SettingsMenu";

/**
 * Das Zahnrad im Kopf.
 *
 * <strong>Der Anlass ist gemessen.</strong> Beim ersten Anlauf hatte der
 * Helligkeitsumschalter im Menü KEINEN zugänglichen Namen — der
 * Barrierefreiheitsbaum zeigte `menuitem` ohne Beschriftung. Ein Vorleser hätte
 * dort „Menüpunkt" gehört und sonst nichts, und die Zeile wäre trotzdem
 * anklickbar gewesen. Genau die Sorte Fehler, die kein Blick auf den Bildschirm
 * findet.
 */
describe("SettingsMenu", () => {
  async function oeffne() {
    const user = userEvent.setup();
    renderMitStore(<SettingsMenu />);
    await user.click(screen.getByRole("button", { name: /Darstellung und Sprache/i }));
    return user;
  }

  it("nennt den Helligkeitsumschalter beim Namen", async () => {
    await oeffne();

    // Der Name, nicht nur die Anwesenheit: ein Menüpunkt ohne Beschriftung ist
    // für die Tastatur da und für den Vorleser nicht.
    expect(
      screen.getByRole("menuitem", { name: /Darstellung wechseln/i }),
    ).toBeInTheDocument();
  });

  it("bietet die vier Sprachen als EINE Wahl an", async () => {
    await oeffne();

    // `menuitemradio` und nicht `menuitem`: es kann genau eine sein. Ohne die
    // Rolle hört ein Vorleser vier gleichwertige Befehle statt einer Auswahl.
    const wahl = screen.getAllByRole("menuitemradio");
    expect(wahl).toHaveLength(4);
    expect(wahl.map((eintrag) => eintrag.textContent)).toEqual([
      "Wie mein Gerät",
      "Deutsch",
      "English",
      "Français",
    ]);
  });

  it("zeigt an, welche Sprache gilt", async () => {
    await oeffne();

    // Ohne Wahl gilt „Wie mein Gerät" — und das muss man SEHEN, sonst ist die
    // Liste eine Liste und keine Einstellung.
    expect(
      screen.getByRole("menuitemradio", { name: "Wie mein Gerät" }),
    ).toBeChecked();
  });

  it("schaltet die Sprache um", async () => {
    const user = await oeffne();

    await user.click(screen.getByRole("menuitemradio", { name: "Français" }));

    await user.click(screen.getByRole("button", { name: /langue|Darstellung und Sprache/i }));
    expect(screen.getByRole("menuitemradio", { name: "Français" })).toBeChecked();
  });
});
