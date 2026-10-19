import { render, screen } from "@testing-library/react";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import { ChecklistsPage } from "./ChecklistsPage";

function draw() {
  return render(
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={buildTheme("light")}>
        <MemoryRouter>
          <ChecklistsPage />
        </MemoryRouter>
      </ThemeProvider>
    </I18nextProvider>,
  );
}

describe("ChecklistsPage", () => {
  it("says before every list that a contract is checked before signing", () => {
    draw();
    expect(screen.getByText(/vor dem Unterschreiben prüfen/i)).toBeInTheDocument();
  });

  it("covers the five topics the catalogue named", () => {
    draw();
    for (const title of ["Kündigen", "Aufhebungsvertrag", "Sperrzeit beim Arbeitslosengeld", "Arbeitszeugnis", "Resturlaub"]) {
      expect(screen.getByRole("heading", { name: title })).toBeInTheDocument();
    }
  });

  it("links neutral places only, and says nobody pays for a referral", () => {
    draw();
    // Every link LEAVING the platform — the one to her own documents stays inside.
    const hosts = screen
      .getAllByRole("link")
      .map((link) => link.getAttribute("href") ?? "")
      .filter((href) => !href.startsWith("/"))
      .map((href) => new URL(href).host);
    expect(hosts).toEqual(["www.arbeitsagentur.de", "anwaltauskunft.de", "www.dgb-rechtsschutz.de", "www.bmas.de"]);
    expect(screen.getByText(/bekommt von niemandem Geld/i)).toBeInTheDocument();
  });
});
