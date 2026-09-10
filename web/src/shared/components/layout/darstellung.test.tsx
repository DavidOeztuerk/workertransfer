import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import { DarstellungsAbschnitte } from "./darstellung";

/**
 * Darstellung und Sprache als zwei Abschnitte mit je einem Auswahlfeld. Die
 * Darstellung hat drei Werte — der Vorgänger konnte nur zweiwertig umschalten,
 * „System" war unerreichbar.
 */
describe("Darstellung und Sprache", () => {
  async function oeffne(feld: string) {
    const user = userEvent.setup();
    renderMitStore(<DarstellungsAbschnitte />);
    await user.click(screen.getByRole("combobox", { name: feld }));
    return user;
  }

  it("bietet die Darstellung mit DREI Werten an, nicht mit zwei", async () => {
    await oeffne("Darstellung");

    const liste = screen.getByRole("listbox");
    expect(within(liste).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "SystemFolgt den Systemeinstellungen",
      "Hell",
      "Dunkel",
    ]);
  });

  it("steht ohne Wahl auf „System“ — und sagt das auch", async () => {
    await oeffne("Darstellung");

    expect(
      within(screen.getByRole("listbox")).getByRole("option", { name: /System/ }),
    ).toHaveAttribute("aria-selected", "true");
  });

  it("schaltet die Darstellung um und behält die Wahl", async () => {
    const user = await oeffne("Darstellung");

    await user.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: "Dunkel" }),
    );

    expect(screen.getByRole("combobox", { name: "Darstellung" })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("bietet die drei Sprachen plus „wie mein Gerät“ an", async () => {
    await oeffne("Sprache");

    const liste = screen.getByRole("listbox");
    expect(within(liste).getAllByRole("option")).toHaveLength(4);
    expect(within(liste).getByRole("option", { name: /Français/ })).toBeInTheDocument();
  });

  it("schaltet die Sprache um", async () => {
    const user = await oeffne("Sprache");

    await user.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: /Français/ }),
    );

    expect(screen.getByRole("combobox", { name: /Langue|Sprache/ })).toHaveTextContent(
      "Français",
    );
  });
});
