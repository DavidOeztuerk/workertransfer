import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Loading } from "./loading";
import { Skeleton } from "./skeleton";

describe("Skeleton", () => {
  // Ein Skeleton ist Dekoration. Sichtbar OHNE Ansage ist es für einen
  // Screenreader ein stummer Bildschirm, deshalb gehört es aus dem
  // Zugänglichkeitsbaum heraus und die Ansage in eine Live-Region daneben.
  //
  // Hier wird ausnahmsweise ein Attribut geprüft und keine Rolle: aria-hidden
  // IST hier das Verhalten — es gibt keine Rolle, die man abfragen könnte.
  it("stays out of the accessibility tree", () => {
    const { container } = render(<Skeleton />);

    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
  });

  it("adds nothing a screen reader would announce", () => {
    render(
      <>
        <Skeleton />
        <Loading label="Profil wird geladen…" />
      </>
    );

    // Genau eine Ansage: die des Loading. Das Skeleton schweigt.
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status")).toHaveTextContent("Profil wird geladen…");
  });

  // Klassenabfrage, weil die Balken absichtlich keine Rolle und keinen Text
  // haben — es gibt nichts anderes, woran man sie zählen könnte.
  it("draws as many bars as asked for", () => {
    const { container } = render(<Skeleton lines={5} />);

    expect(container.querySelectorAll(".wt-skeleton__bar")).toHaveLength(5);
  });

  it("draws three bars without being asked", () => {
    const { container } = render(<Skeleton />);

    expect(container.querySelectorAll(".wt-skeleton__bar")).toHaveLength(3);
  });
});
