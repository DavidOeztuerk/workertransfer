import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Select } from "./select";

function Options() {
  return (
    <>
      <option value="">Alle</option>
      <option value="remote">Nur remote</option>
      <option value="onsite">Nur vor Ort</option>
    </>
  );
}

describe("Select", () => {
  it("is reachable by its label", () => {
    render(
      <Select label="Arbeitsform" value="" onChange={() => {}}>
        <Options />
      </Select>
    );

    expect(screen.getByRole("combobox", { name: "Arbeitsform" })).toBeInTheDocument();
  });

  // Das ist der Grund für das native Element: die Tastaturbedienung kommt vom
  // Browser und muss nicht nachgebaut werden.
  it("can be operated with the keyboard", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <Select label="Arbeitsform" value="" onChange={onChange}>
        <Options />
      </Select>
    );

    await user.selectOptions(screen.getByRole("combobox"), "remote");

    expect(onChange).toHaveBeenCalled();
  });

  // Wie bei Field: Hinweis UND Fehler werden verknüpft. aria-describedby
  // ersetzt sonst das eine durch das andere, und dann hört man den Fehler nicht.
  it("links hint and error together, not one instead of the other", () => {
    render(
      <Select
        label="Rolle"
        hint="Administratoren dürfen einladen."
        error="Bitte eine Rolle wählen."
        value=""
        onChange={() => {}}
      >
        <Options />
      </Select>
    );

    const control = screen.getByRole("combobox", { name: "Rolle" });
    expect(control).toHaveAccessibleDescription(
      "Administratoren dürfen einladen. Bitte eine Rolle wählen."
    );
    expect(control).toBeInvalid();
  });

  it("stays valid while there is no error", () => {
    render(
      <Select label="Arbeitsform" value="" onChange={() => {}}>
        <Options />
      </Select>
    );

    expect(screen.getByRole("combobox")).toBeValid();
  });
});
