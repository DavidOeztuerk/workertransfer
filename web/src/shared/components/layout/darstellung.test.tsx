import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import { DarstellungsAbschnitte } from "./darstellung";

/**
 * Darstellung und Sprache, als zwei benannte Abschnitte mit je einem
 * Auswahlfeld.
 *
 * <strong>Der Vorgänger war ein Zahnrad mit einer flachen Liste</strong>, und
 * er hatte einen Fehler, den erst diese Fassung sichtbar macht: die Darstellung
 * kannte dort nur ein zweiwertiges Umschalten. Der Store führt seit jeher
 * `system | light | dark` — „System" war vorhanden und über die Oberfläche
 * nicht erreichbar. Wer einmal umschaltete, kam nie wieder zurück zu „folgt dem
 * Gerät", ohne den lokalen Speicher zu leeren.
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

    // Ohne diesen Wert wäre die Voreinstellung unsichtbar, und die Wahl sähe
    // aus wie „hell", obwohl niemand hell gewählt hat.
    expect(
      within(screen.getByRole("listbox")).getByRole("option", { name: /System/ }),
    ).toHaveAttribute("aria-selected", "true");
  });

  it("schaltet die Darstellung um und behält die Wahl", async () => {
    const user = await oeffne("Darstellung");

    await user.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: "Dunkel" }),
    );

    // Zugeklappt muss stehen, was GILT — sonst ist das Feld eine Liste und
    // keine Einstellung.
    expect(screen.getByRole("combobox", { name: "Darstellung" })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("bietet die drei Sprachen plus „wie mein Gerät“ an", async () => {
    await oeffne("Sprache");

    const liste = screen.getByRole("listbox");
    expect(within(liste).getAllByRole("option")).toHaveLength(4);
    // Der Name der Sprache steht IN dieser Sprache: wer die Oberfläche gerade
    // nicht lesen kann, sucht „Deutsch", nicht „German".
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
