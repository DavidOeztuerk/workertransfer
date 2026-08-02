import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Portfolio } from "../portfolio/client";
import { renderWithProviders } from "../test/render";
import { PortfolioRoute } from "./portfolio";

vi.mock("../portfolio/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../portfolio/client")>();
  return {
    ...actual,
    getMyPortfolio: vi.fn(),
    saveMyPortfolio: vi.fn(),
    getPortfolioVisibility: vi.fn(),
    setPortfolioVisibility: vi.fn(),
  };
});

const client = await import("../portfolio/client");
const getMyPortfolio = vi.mocked(client.getMyPortfolio);
const saveMyPortfolio = vi.mocked(client.saveMyPortfolio);
const getPortfolioVisibility = vi.mocked(client.getPortfolioVisibility);
const setPortfolioVisibility = vi.mocked(client.setPortfolioVisibility);

const SUBJECT = "11111111-1111-1111-1111-111111111111";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function portfolio(): Portfolio {
  return {
    subject_id: SUBJECT,
    items: [
      {
        title: "Ein Werkzeug",
        summary: "Was es tut.",
        url: "https://example.org/werkzeug",
        role: "Entwicklung",
        year: 2024,
      },
    ],
    updated_at: "2026-08-02T10:00:00Z",
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyPortfolio.mockResolvedValue(null);
  getPortfolioVisibility.mockResolvedValue(false);
  saveMyPortfolio.mockResolvedValue({ ok: true, portfolio: portfolio() });
  setPortfolioVisibility.mockResolvedValue({ ok: true, granted: true });
});

describe("PortfolioRoute", () => {
  it("fills the form with what is already stored", async () => {
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioRoute principal={principal()} />);

    expect(await screen.findByDisplayValue("Ein Werkzeug")).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://example.org/werkzeug")).toBeInTheDocument();
  });

  it("sends an empty link as null, not as an empty string", async () => {
    // "" würde später als Link gerendert und ins Nichts führen.
    const user = userEvent.setup();
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Arbeit hinzufügen/i }));
    await user.type(screen.getByLabelText(/Titel/i), "Ohne Link");
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    expect(saveMyPortfolio.mock.calls[0]?.[0][0]?.url).toBeNull();
  });

  it("sends an empty year as null, not as zero", async () => {
    const user = userEvent.setup();
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Arbeit hinzufügen/i }));
    await user.type(screen.getByLabelText(/Titel/i), "Ohne Jahr");
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    expect(saveMyPortfolio.mock.calls[0]?.[0][0]?.year).toBeNull();
  });

  it("keeps a rejected link on screen and does not claim success", async () => {
    const user = userEvent.setup();
    getMyPortfolio.mockResolvedValue(portfolio());
    saveMyPortfolio.mockResolvedValue({
      ok: false,
      reason: "invalid",
      message: "Only http and https links are allowed",
    });
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Speichern$/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("http and https");
    expect(screen.queryByText(/gespeichert/i)).toBeNull();
  });

  it("switches its own release, not the profile's", async () => {
    const user = userEvent.setup();
    getMyPortfolio.mockResolvedValue(portfolio());
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await waitFor(() => expect(screen.getByRole("switch")).not.toBeChecked());
    await user.click(screen.getByRole("switch"));

    await waitFor(() => expect(setPortfolioVisibility).toHaveBeenCalledWith(SUBJECT, true));
  });

  it("does not offer a release before there is something to release", async () => {
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await screen.findByRole("button", { name: /Arbeit hinzufügen/i });
    expect(screen.getByRole("switch")).toBeDisabled();
  });

  it("tells an anonymous visitor to log in", () => {
    renderWithProviders(<PortfolioRoute principal={null} />);

    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });
});
