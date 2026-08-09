import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { Toast } from "./toast";

describe("Toast", () => {
  it("announces itself politely", () => {
    render(<Toast message="Gespeichert." onDismiss={() => {}} />);

    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("Gespeichert.");
  });

  it("can always be dismissed by hand", async () => {
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    render(<Toast message="Gespeichert." onDismiss={onDismiss} />);

    await user.click(screen.getByRole("button", { name: "Ausblenden" }));

    expect(onDismiss).toHaveBeenCalled();
  });

  describe("mit Zeitgrenze", () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    // Kein automatisches Verschwinden als Standard: eine Meldung, die von
    // selbst geht, ist schlechter als eine, die stehen bleibt, sobald jemand
    // danach handeln muss.
    it("stays until dismissed when no duration is given", () => {
      const onDismiss = vi.fn();
      render(<Toast message="Gespeichert." onDismiss={onDismiss} />);

      vi.advanceTimersByTime(60_000);

      expect(onDismiss).not.toHaveBeenCalled();
    });

    it("goes on its own only when asked to", () => {
      const onDismiss = vi.fn();
      render(<Toast message="Gespeichert." onDismiss={onDismiss} durationMs={4000} />);

      expect(onDismiss).not.toHaveBeenCalled();
      vi.advanceTimersByTime(4000);
      expect(onDismiss).toHaveBeenCalledTimes(1);
    });
  });
});
