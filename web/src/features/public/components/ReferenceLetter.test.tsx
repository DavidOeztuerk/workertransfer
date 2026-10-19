import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import { ReferenceLetter } from "./ReferenceLetter";

function draw() {
  return render(
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={buildTheme("light")}>
        <MemoryRouter>
          <ReferenceLetter />
        </MemoryRouter>
      </ThemeProvider>
    </I18nextProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const letter = () => screen.getByLabelText("Dein Schreiben") as HTMLTextAreaElement;

describe("the sample letter for a reference", () => {
  it("fills the final reference in the browser and sends nothing anywhere", async () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);
    draw();

    expect(letter().value).toContain("§ 109 Gewerbeordnung");
    expect(letter().value).toContain("[Dein Name]");

    await userEvent.type(screen.getByLabelText("Dein Name"), "Anna Schmidt");

    expect(letter().value).toContain("Anna Schmidt");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("shows the interim letter only after the risk was acknowledged", async () => {
    draw();

    await userEvent.click(screen.getByRole("radio", { name: /Zwischenzeugnis/ }));

    expect(screen.getByText(/verrät seinem Arbeitgeber oft, dass er sich umsieht/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Dein Schreiben")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("checkbox", { name: /Mir ist bewusst/ }));

    expect(letter().value).toContain("Zwischenzeugnis");
    expect(letter().value).toContain("hiermit bitte ich Sie");
  });
});
