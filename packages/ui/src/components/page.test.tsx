import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Page } from "./page";

describe("Page", () => {
  it("carries exactly one first-level heading", () => {
    render(
      <Page title="Offene Stellen">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("heading", { level: 1, name: "Offene Stellen" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  // Das <main> trägt den Namen der Seite. Ohne aria-labelledby heißt der
  // Hauptbereich für einen Screenreader nur „main" — bei 26 Seiten hilft das
  // niemandem beim Erkennen, wo er ist.
  it("names its main region after the heading", () => {
    render(
      <Page title="Meine Freigaben">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("main", { name: "Meine Freigaben" })).toBeInTheDocument();
  });

  it("shows lead and note when given", () => {
    render(
      <Page title="Lebenslauf" lead="Wer fragt, bekommt eine Antwort." note="Widerruf jederzeit.">
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByText("Wer fragt, bekommt eine Antwort.")).toBeInTheDocument();
    expect(screen.getByText("Widerruf jederzeit.")).toBeInTheDocument();
  });

  // Eigene Routen für Formulare brauchen einen Rückweg — ein <main> ohne ist
  // auf einem Deep-Link eine Sackgasse.
  it("can offer a way back", () => {
    render(
      <Page title="Bewerben" back={<a href="/jobs">Zurück zu den Stellen</a>}>
        <p>Inhalt</p>
      </Page>
    );

    expect(screen.getByRole("link", { name: "Zurück zu den Stellen" })).toBeInTheDocument();
  });

  it("renders its children", () => {
    render(
      <Page title="Stellen">
        <p>Eine Stelle</p>
      </Page>
    );

    expect(screen.getByText("Eine Stelle")).toBeInTheDocument();
  });
});
