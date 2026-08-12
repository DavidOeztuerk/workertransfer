import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Checkbox } from "./checkbox";
import { Switch } from "./switch";

describe("Checkbox", () => {
  it("is reachable by its label", () => {
    render(<Checkbox label="Lebenslauf" checked onChange={() => {}} />);

    expect(screen.getByRole("checkbox", { name: "Lebenslauf" })).toBeChecked();
  });

  it("toggles on click", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Checkbox label="Meine Arbeiten" checked={false} onChange={onChange} />);

    await user.click(screen.getByRole("checkbox"));

    expect(onChange).toHaveBeenCalled();
  });

  it("links its hint for a screen reader", () => {
    render(
      <Checkbox
        label="Meine Arbeiten"
        hint="Auch die hochgeladenen Dateien."
        checked={false}
        onChange={() => {}}
      />
    );

    expect(screen.getByRole("checkbox")).toHaveAccessibleDescription(
      "Auch die hochgeladenen Dateien."
    );
  });

  // Die Unterscheidung ist an beiden Enden festgenagelt, weil sie beim
  // „Aufräumen" wie eine Doppelung aussieht und keine ist: eine Checkbox
  // verspricht, dass die Änderung erst mit dem Absenden gilt; ein Switch gilt
  // sofort. Bei einer Einwilligung entscheidet dieses Versprechen, was die
  // Person glaubt getan zu haben.
  it("is a checkbox and not a switch — the promise differs", () => {
    render(
      <>
        <Checkbox label="Lebenslauf" checked={false} onChange={() => {}} />
        <Switch label="Profil freigeben" checked={false} onChange={() => {}} />
      </>
    );

    expect(screen.getByRole("checkbox", { name: "Lebenslauf" })).toBeInTheDocument();
    expect(screen.getByRole("switch", { name: "Profil freigeben" })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Profil freigeben" })).toBeNull();
  });
});
