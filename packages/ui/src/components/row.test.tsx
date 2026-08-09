import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Row, RowList } from "./row";

describe("RowList und Row", () => {
  // Eine Liste als Liste: ein Screenreader sagt dann „Liste, 3 Einträge" und
  // die Person weiß, wie viel kommt. Drei divs sagen nichts.
  it("is a list with counted items", () => {
    render(
      <RowList>
        <Row title="Bäckerei Kern" />
        <Row title="Stadtwerke" />
        <Row title="Klinikum Nord" />
      </RowList>
    );

    expect(screen.getByRole("list")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });

  it("shows title, meta and actions", () => {
    render(
      <RowList>
        <Row
          title="Bäckerei Kern"
          meta="Angefragt am 3. August"
          actions={<button type="button">Freigeben</button>}
        />
      </RowList>
    );

    expect(screen.getByText("Bäckerei Kern")).toBeInTheDocument();
    expect(screen.getByText("Angefragt am 3. August")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Freigeben" })).toBeInTheDocument();
  });

  it("works without meta and without actions", () => {
    render(
      <RowList>
        <Row title="Nur ein Titel" />
      </RowList>
    );

    expect(screen.getByRole("listitem")).toHaveTextContent("Nur ein Titel");
  });
});
