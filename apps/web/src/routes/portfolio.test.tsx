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
    uploadAttachment: vi.fn(),
  };
});

const client = await import("../portfolio/client");
const getMyPortfolio = vi.mocked(client.getMyPortfolio);
const saveMyPortfolio = vi.mocked(client.saveMyPortfolio);
const getPortfolioVisibility = vi.mocked(client.getPortfolioVisibility);
const setPortfolioVisibility = vi.mocked(client.setPortfolioVisibility);
const uploadAttachment = vi.mocked(client.uploadAttachment);

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

describe("PortfolioRoute — Anhänge", () => {
  it("uploads immediately and keeps the name for the save", async () => {
    const user = userEvent.setup();
    uploadAttachment.mockResolvedValue({
      ok: true,
      name: "abc123.png",
      contentType: "image/png",
      size: 4,
    });
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Arbeit hinzufügen/i }));
    await user.type(screen.getByLabelText(/Titel/i), "Mit Datei");
    await user.upload(
      screen.getByLabelText("Datei"),
      new File([new Uint8Array([1, 2, 3, 4])], "bild.png", { type: "image/png" })
    );
    await screen.findByText(/Datei angehängt/i);
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    expect(saveMyPortfolio.mock.calls[0]?.[0][0]?.attachment).toBe("abc123.png");
  });

  it("never shows the local file name — it never went to the server", async () => {
    const user = userEvent.setup();
    uploadAttachment.mockResolvedValue({
      ok: true,
      name: "abc123.png",
      contentType: "image/png",
      size: 4,
    });
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Arbeit hinzufügen/i }));
    await user.upload(
      screen.getByLabelText("Datei"),
      new File([new Uint8Array([1])], "streng-geheim.png", { type: "image/png" })
    );

    await screen.findByText(/Datei angehängt/i);
    expect(screen.queryByText(/streng-geheim/)).toBeNull();
  });

  it("says why a file was refused instead of failing quietly", async () => {
    // Die Datei trägt einen erlaubten Typ und heißt .png — nur ihre Bytes sind
    // HTML. Genau so sieht der Angriff aus, und genau deshalb entscheidet der
    // SERVER: `accept` im Dialog ist eine Bequemlichkeit, keine Prüfung. (Sie
    // filtert im Test sogar so gut, dass eine .txt-Datei gar nicht erst
    // ankäme — der interessante Fall kommt an und wird trotzdem abgelehnt.)
    const user = userEvent.setup();
    uploadAttachment.mockResolvedValue({
      ok: false,
      message: "Only PNG, JPEG and PDF files are accepted",
    });
    renderWithProviders(<PortfolioRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Arbeit hinzufügen/i }));
    await user.upload(
      screen.getByLabelText("Datei"),
      new File(["<html><script>alert(1)</script></html>"], "harmlos.png", {
        type: "image/png",
      })
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("PNG");
  });
});
