import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Switch } from "./switch";

describe("Switch", () => {
  it("announces itself as a switch with its state", () => {
    render(<Switch label="Profil freigeben" checked onChange={() => {}} />);

    const control = screen.getByRole("switch", { name: "Profil freigeben" });
    expect(control).toBeChecked();
  });

  it("reports the state it would move to, not the one it is in", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Switch label="Profil freigeben" checked={false} onChange={onChange} />);

    await user.click(screen.getByRole("switch"));

    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("stays silent while disabled", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Switch label="Profil freigeben" checked={false} disabled onChange={onChange} />);

    await user.click(screen.getByRole("switch"));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("links its hint so a screen reader gets the reason it is off", () => {
    render(
      <Switch
        label="Profil freigeben"
        checked={false}
        disabled
        hint="Erst ein Profil anlegen."
        onChange={() => {}}
      />
    );

    expect(screen.getByRole("switch")).toHaveAccessibleDescription("Erst ein Profil anlegen.");
  });
});
