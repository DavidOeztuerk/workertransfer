import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Empty } from "./empty";

describe("Empty", () => {
  it("says what is not there", () => {
    render(<Empty title="Es läuft gerade kein Gespräch." />);

    expect(screen.getByText("Es läuft gerade kein Gespräch.")).toBeInTheDocument();
  });

  // Ein Leerzustand ist KEIN Fehler und KEIN Ladevorgang. Bekäme er
  // role="status", würde jede leere Liste beim Aufbau der Seite vorgelesen.
  it("is neither an alert nor a status", () => {
    render(<Empty title="Noch keine Arbeiten." />);

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("can offer the way out", () => {
    render(
      <Empty
        title="Noch keine Arbeiten."
        hint="Was du hier ablegst, sieht nur, wem du es freigibst."
        action={<a href="/portfolio">Arbeit hinzufügen</a>}
      />
    );

    expect(screen.getByRole("link", { name: "Arbeit hinzufügen" })).toBeInTheDocument();
    expect(
      screen.getByText("Was du hier ablegst, sieht nur, wem du es freigibst.")
    ).toBeInTheDocument();
  });
});
