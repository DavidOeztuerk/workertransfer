import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { Fieldset, RadioGroup } from "./fieldset";

const MARKT = [
  { value: "quiet", label: "Ruhig", hint: "Niemand spricht dich an." },
  { value: "open", label: "Offen", hint: "Unternehmen dürfen fragen." },
  { value: "active", label: "Aktiv", hint: "Du suchst gerade." },
];

describe("Fieldset", () => {
  it("names the group with its legend", () => {
    render(
      <Fieldset legend="Station">
        <input aria-label="Arbeitgeber" />
      </Fieldset>
    );

    expect(screen.getByRole("group", { name: "Station" })).toBeInTheDocument();
  });
});

describe("RadioGroup", () => {
  it("names the group and offers one radio per option", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    expect(screen.getByRole("group", { name: /Marktstatus/ })).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(3);
    expect(screen.getByRole("radio", { name: "Ruhig" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Offen" })).not.toBeChecked();
  });

  it("reports the chosen value, not the event", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={onChange}
        options={MARKT}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Aktiv" }));

    expect(onChange).toHaveBeenCalledWith("active");
  });

  // Der gemeinsame `name` ist das, was den Browser die Gruppe bilden lässt —
  // und erst dadurch bewegen die Pfeiltasten den Fokus zwischen den
  // Auswahlknöpfen, statt sie einzeln anzutabben. Die Pfeiltastenbedienung
  // selbst ist die des Browsers; hier wird geprüft, dass sie zustande kommt.
  it("groups the radios under one name so the browser wires the arrow keys", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    for (const radio of screen.getAllByRole("radio")) {
      expect(radio).toHaveAttribute("name", "markt");
    }
  });

  it("explains each option, because three words are not three choices", () => {
    render(
      <RadioGroup
        legend="Marktstatus"
        name="markt"
        value="quiet"
        onChange={() => {}}
        options={MARKT}
      />
    );

    expect(screen.getByRole("radio", { name: "Offen" })).toHaveAccessibleDescription(
      "Unternehmen dürfen fragen."
    );
  });
});
