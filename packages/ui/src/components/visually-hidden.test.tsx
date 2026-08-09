import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { VisuallyHidden } from "./visually-hidden";

describe("VisuallyHidden", () => {
  // Das Gegenteil von aria-hidden, und der Unterschied ist der ganze Zweck:
  // hier bleibt der Text FÜR DEN VORLESER da und verschwindet nur für das
  // Auge. Ein aria-hidden hier würde die Wörter hinter ✓ und ✗ unterschlagen.
  it("keeps its text in the accessibility tree", () => {
    render(
      <p>
        Python
        <VisuallyHidden> (hast du)</VisuallyHidden>
      </p>
    );

    expect(screen.getByText("(hast du)")).toBeInTheDocument();
  });

  it("does not hide from assistive technology", () => {
    const { container } = render(<VisuallyHidden>Nur zum Hören</VisuallyHidden>);

    expect(container.firstElementChild).not.toHaveAttribute("aria-hidden");
  });
});
