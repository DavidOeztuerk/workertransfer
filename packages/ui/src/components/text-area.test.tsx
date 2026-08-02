import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { TextArea } from "./text-area";

describe("TextArea", () => {
  it("is reachable by its label", () => {
    render(<TextArea label="Über mich" value="" onChange={() => {}} />);

    expect(screen.getByLabelText("Über mich")).toBeInTheDocument();
  });

  it("reads out hint and error together, not one instead of the other", () => {
    render(
      <TextArea
        label="Über mich"
        hint="Was dich ausmacht."
        error="Zu lang."
        value=""
        onChange={() => {}}
      />
    );

    const field = screen.getByLabelText("Über mich");
    expect(field).toHaveAccessibleDescription("Was dich ausmacht. Zu lang.");
    expect(field).toHaveAttribute("aria-invalid", "true");
  });
});
