import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Loading } from "./loading";

describe("Loading", () => {
  // role="status" und nicht role="alert": ein Ladevorgang unterbricht nicht,
  // er wird berichtet, sobald der Screenreader Luft hat.
  it("reports itself politely as a status, not as an alert", () => {
    render(<Loading label="Portfolio wird geladen…" />);

    expect(screen.getByRole("status")).toHaveTextContent("Portfolio wird geladen…");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  // Das Label kommt vom Aufrufer. Ein hartkodiertes „Wird geladen…" nähme der
  // Seite die Auskunft, WAS lädt — und der Bestand sagt es je Route anders.
  it("says what is loading, not that something is", () => {
    render(<Loading label="Marktstatus wird geladen…" />);

    expect(screen.getByRole("status")).toHaveTextContent("Marktstatus wird geladen…");
  });
});
