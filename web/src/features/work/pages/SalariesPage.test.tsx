import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { afterEach, describe, expect, it, vi } from "vitest";

import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import { SalariesPage } from "./SalariesPage";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const ADVERT = (id: string, min: number, max: number) => ({
  id,
  tenant_id: "t",
  title: `Schweißer ${id}`,
  description: "",
  location: "Kiel",
  postal_code: "",
  remote_mode: "none",
  employment_type: "full_time",
  skills: [],
  status: "published",
  published_at: null,
  updated_at: "",
  salary: { min, max, period: "month", currency: "EUR" },
});

describe("SalariesPage", () => {
  it("lists each advert's own range with its source, and computes nothing", async () => {
    const urls: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: string | URL | Request) => {
        urls.push(String(input));
        return new Response(
          JSON.stringify({ items: [ADVERT("a", 3000, 3400), ADVERT("b", 2800, 3800)], page: 1, page_size: 50 }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }),
    );

    render(
      <I18nextProvider i18n={i18n}>
        <ThemeProvider theme={buildTheme("light")}>
          <SalariesPage />
        </ThemeProvider>
      </I18nextProvider>,
    );

    await userEvent.type(screen.getByLabelText("Tätigkeit oder Titel"), "Schweißer");
    await userEvent.click(screen.getByRole("button", { name: "Vergleichen" }));

    expect(await screen.findByText(/3\.000\s€ – 3\.400\s€ brutto\/Monat/)).toBeInTheDocument();
    expect(screen.getByText(/2\.800\s€ – 3\.800\s€ brutto\/Monat/)).toBeInTheDocument();
    expect(screen.getByText(/Quelle: 2 veröffentlichte Inserate/)).toBeInTheDocument();
    expect(urls[0]).toContain("with_salary=true");
    // No computed figure: neither 3.000 + 2.800 averaged nor a median.
    expect(screen.queryByText(/3\.250|3\.300|Median|Mittelwert/)).not.toBeInTheDocument();
    // The official source is named and said to be not connected.
    expect(screen.getByText(/Noch nicht angebunden/)).toBeInTheDocument();
  });
});
