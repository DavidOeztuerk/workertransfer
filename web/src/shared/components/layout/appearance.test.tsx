import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderWithStore } from "../../../features/auth/test/render";
import { AppearanceSections } from "./appearance";

/**
 * Appearance and language as two sections with one select field each. The
 * appearance has three values — the predecessor could only toggle between two,
 * "System" was unreachable.
 */
describe("appearance and language", () => {
  async function open(field: string) {
    const user = userEvent.setup();
    renderWithStore(<AppearanceSections />);
    await user.click(screen.getByRole("combobox", { name: field }));
    return user;
  }

  it("offers the appearance with THREE values, not two", async () => {
    await open("Darstellung");

    const list = screen.getByRole("listbox");
    expect(within(list).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "SystemFolgt den Systemeinstellungen",
      "Hell",
      "Dunkel",
    ]);
  });

  it("stands on \"System\" without a choice — and says so", async () => {
    await open("Darstellung");

    expect(
      within(screen.getByRole("listbox")).getByRole("option", { name: /System/ }),
    ).toHaveAttribute("aria-selected", "true");
  });

  it("switches the appearance and keeps the choice", async () => {
    const user = await open("Darstellung");

    await user.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: "Dunkel" }),
    );

    expect(screen.getByRole("combobox", { name: "Darstellung" })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("offers the three languages plus \"like my device\"", async () => {
    await open("Sprache");

    const list = screen.getByRole("listbox");
    expect(within(list).getAllByRole("option")).toHaveLength(4);
    expect(within(list).getByRole("option", { name: /Français/ })).toBeInTheDocument();
  });

  it("switches the language", async () => {
    const user = await open("Sprache");

    await user.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: /Français/ }),
    );

    expect(screen.getByRole("combobox", { name: /Langue|Sprache/ })).toHaveTextContent(
      "Français",
    );
  });
});
