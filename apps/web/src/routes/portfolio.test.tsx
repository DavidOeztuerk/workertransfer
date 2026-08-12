import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Portfolio } from "../portfolio/client";
import { renderWithProviders } from "../test/render";
import { PortfolioRoute } from "./portfolio";

// Die Formulare liegen auf eigenen Adressen; ihre Zusagen stehen in
// `portfolio-item.test.tsx` — Anhänge, leerer Link als `null`, leeres Jahr als
// `null`, der abgelehnte Link und das Entfernen genau einer Arbeit.
vi.mock("../portfolio/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../portfolio/client")>();
  return {
    ...actual,
    getMyPortfolio: vi.fn(),
    getPortfolioVisibility: vi.fn(),
    setPortfolioVisibility: vi.fn(),
  };
});

const client = await import("../portfolio/client");
const getMyPortfolio = vi.mocked(client.getMyPortfolio);
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
        attachment: null,
      },
    ],
    updated_at: "2026-08-02T10:00:00Z",
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyPortfolio.mockResolvedValue(null);
  getPortfolioVisibility.mockResolvedValue(false);
  setPortfolioVisibility.mockResolvedValue({ ok: true, granted: true });
});

describe("PortfolioRoute", () => {
  it("listet die Arbeiten und verlinkt jede auf ihre eigene Adresse", async () => {
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioRoute principal={principal()} />);

    expect(await screen.findByText("Ein Werkzeug")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Bearbeiten/i })).toHaveAttribute(
      "href",
      "/portfolio/0"
    );
    expect(screen.getByRole("link", { name: /Arbeit hinzufügen/i })).toHaveAttribute(
      "href",
      "/portfolio/new"
    );
  });

  it("bietet in der Liste kein Entfernen an", async () => {
    // Das Entfernen steht auf der Seite der einzelnen Arbeit: dort hat die
    // Person sie vor sich und sieht, was verschwindet. In einer Liste wäre es
    // ein Knopf neben einer Zeile, und Zeilen verwechselt man.
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await screen.findByText("Ein Werkzeug");
    expect(screen.queryByRole("button", { name: /entfernen/i })).toBeNull();
  });

  it("sagt bei leerem Portfolio, dass die Voreinstellung kein Fehler ist", async () => {
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    expect(await screen.findByText(/Noch keine Arbeit eingetragen/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Arbeit hinzufügen/i })).toBeInTheDocument();
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

    await screen.findByText(/Noch keine Arbeit eingetragen/i);
    expect(screen.getByRole("switch")).toBeDisabled();
  });

  it("sperrt den Schalter, wenn der Ledger nicht antwortet — und sagt es", async () => {
    // Dieselbe Korrektur wie auf der Profilseite: `null` heißt „der Ledger hat
    // nicht geantwortet". Vorher war das von „nicht freigegeben" nicht zu
    // unterscheiden, und der Schalter war in dieser Lage BEDIENBAR — der
    // nächste Klick hätte etwas freigegeben, dessen Stand niemand kennt.
    //
    // MIT gespeicherter Arbeit, sonst wäre der Schalter ohnehin gesperrt und
    // dieser Test grün, ohne etwas zu prüfen.
    getMyPortfolio.mockResolvedValue(portfolio());
    getPortfolioVisibility.mockResolvedValue(null);

    renderWithProviders(<PortfolioRoute principal={principal()} />);

    const schalter = await screen.findByRole("switch");
    await waitFor(() => expect(schalter).toBeDisabled());
    expect(schalter).toHaveAttribute("aria-checked", "false");
    expect(screen.getByText(/nicht abrufbar/i)).toBeInTheDocument();
    // Und der Grund ist der richtige: nicht „erst eine Arbeit speichern".
    expect(screen.queryByText(/Erst eine Arbeit speichern/i)).toBeNull();
  });

  it("tells an anonymous visitor to log in", () => {
    renderWithProviders(<PortfolioRoute principal={null} />);

    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });
});
