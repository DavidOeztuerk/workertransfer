import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Portfolio } from "../portfolio/client";
import { renderWithProviders } from "../test/render";
import { PortfolioItemRoute } from "./portfolio-item";

vi.mock("../portfolio/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../portfolio/client")>();
  return {
    ...actual,
    getMyPortfolio: vi.fn(),
    saveMyPortfolio: vi.fn(),
    uploadAttachment: vi.fn(),
  };
});

const client = await import("../portfolio/client");
const getMyPortfolio = vi.mocked(client.getMyPortfolio);
const saveMyPortfolio = vi.mocked(client.saveMyPortfolio);
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

// jsdom kann keine echte Navigation; ohne diesen Ersatz schreibt es
// „Not implemented: navigation to another Document" auf die Konsole, und über
// das Ziel nach dem Speichern könnte kein Test etwas behaupten.
function stubLocation(): { readonly href: string } {
  let href = "";
  const stub = {
    get href() {
      return href;
    },
    set href(value: string) {
      href = value;
    },
  };
  Object.defineProperty(window, "location", { value: stub, writable: true, configurable: true });
  return {
    get href() {
      return href;
    },
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyPortfolio.mockResolvedValue(null);
  saveMyPortfolio.mockResolvedValue({ ok: true, portfolio: portfolio() });
});

describe("PortfolioItemRoute — anlegen", () => {
  it("sends an empty link as null, not as an empty string", async () => {
    // "" würde später als Link gerendert und ins Nichts führen.
    const user = userEvent.setup();
    stubLocation();
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Ohne Link");
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    expect(saveMyPortfolio.mock.calls[0]?.[0][0]?.url).toBeNull();
  });

  it("sends an empty year as null, not as zero", async () => {
    const user = userEvent.setup();
    stubLocation();
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Ohne Jahr");
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    expect(saveMyPortfolio.mock.calls[0]?.[0][0]?.year).toBeNull();
  });

  it("hängt an, statt zu ersetzen — vorhandene Arbeiten bleiben stehen", async () => {
    // Gespeichert wird das ganze Feld (PUT). Würde diese Seite nur die eine
    // Arbeit schicken, wären beim ersten Anlegen alle anderen weg.
    const user = userEvent.setup();
    stubLocation();
    getMyPortfolio.mockResolvedValue(portfolio());
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Die zweite");
    await user.click(screen.getByRole("button", { name: /^Speichern$/ }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    const geschickt = saveMyPortfolio.mock.calls[0]?.[0] ?? [];
    expect(geschickt).toHaveLength(2);
    expect(geschickt[0]?.title).toBe("Ein Werkzeug");
    expect(geschickt[1]?.title).toBe("Die zweite");
  });

  it("bietet beim Anlegen kein Entfernen an", async () => {
    // Es gibt nichts zu entfernen, und ein Knopf dafür wäre die Behauptung, es
    // gäbe schon etwas.
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await screen.findByLabelText(/Titel/i);
    expect(screen.queryByRole("button", { name: /entfernen/i })).toBeNull();
  });
});

describe("PortfolioItemRoute — ändern", () => {
  it("fills the form with what is already stored", async () => {
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioItemRoute index="0" principal={principal()} />);

    expect(await screen.findByDisplayValue("Ein Werkzeug")).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://example.org/werkzeug")).toBeInTheDocument();
  });

  it("nennt die Arbeit in der Überschrift", async () => {
    // Die Adresse ist die Stelle im Feld, nicht eine ID (`PortfolioItem` hat
    // keine). Wer `/portfolio/0` öffnet und dort eine andere Arbeit liest als
    // erwartet, muss es sofort sehen — das ist die Gegenmaßnahme.
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioItemRoute index="0" principal={principal()} />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Ein Werkzeug" })
    ).toBeInTheDocument();
  });

  it("keeps a rejected link on screen and does not claim success", async () => {
    const user = userEvent.setup();
    getMyPortfolio.mockResolvedValue(portfolio());
    saveMyPortfolio.mockResolvedValue({
      ok: false,
      reason: "invalid",
      message: "Only http and https links are allowed",
    });
    renderWithProviders(<PortfolioItemRoute index="0" principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Speichern$/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent("http and https");
    expect(screen.queryByText(/gespeichert/i)).toBeNull();
  });

  it("entfernt genau diese eine Arbeit", async () => {
    const user = userEvent.setup();
    stubLocation();
    getMyPortfolio.mockResolvedValue({
      ...portfolio(),
      items: [
        ...portfolio().items,
        { title: "Die andere", summary: "", url: null, role: "", year: null, attachment: null },
      ],
    });
    renderWithProviders(<PortfolioItemRoute index="0" principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Diese Arbeit entfernen/i }));

    await waitFor(() => expect(saveMyPortfolio).toHaveBeenCalled());
    const geschickt = saveMyPortfolio.mock.calls[0]?.[0] ?? [];
    expect(geschickt).toHaveLength(1);
    expect(geschickt[0]?.title).toBe("Die andere");
  });

  it("macht aus einer Adresse ohne Arbeit kein leeres Formular", async () => {
    // Etwa nach dem Entfernen in einem anderen Tab. Ein Formular anzubieten
    // würde daraus stillschweigend eine NEUE Arbeit machen, und die Person
    // hätte eine angelegt, ohne es zu wollen.
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioItemRoute index="7" principal={principal()} />);

    expect(
      await screen.findByRole("heading", { level: 1, name: /Diese Arbeit gibt es nicht/i })
    ).toBeInTheDocument();
    expect(screen.queryByLabelText(/Titel/i)).toBeNull();
  });

  it("bietet den Rückweg an", async () => {
    getMyPortfolio.mockResolvedValue(portfolio());

    renderWithProviders(<PortfolioItemRoute index="0" principal={principal()} />);

    await screen.findByLabelText(/Titel/i);
    expect(screen.getByRole("link", { name: /Zurück zu meinen Arbeiten/i })).toHaveAttribute(
      "href",
      "/portfolio"
    );
  });

  it("tells an anonymous visitor to log in", () => {
    renderWithProviders(<PortfolioItemRoute index="new" principal={null} />);

    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });
});

describe("PortfolioItemRoute — Anhänge", () => {
  it("uploads immediately and keeps the name for the save", async () => {
    const user = userEvent.setup();
    stubLocation();
    uploadAttachment.mockResolvedValue({
      ok: true,
      name: "abc123.png",
      contentType: "image/png",
      size: 4,
    });
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Mit Datei");
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
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.upload(
      await screen.findByLabelText("Datei"),
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
    renderWithProviders(<PortfolioItemRoute index="new" principal={principal()} />);

    await user.upload(
      await screen.findByLabelText("Datei"),
      new File(["<html><script>alert(1)</script></html>"], "harmlos.png", {
        type: "image/png",
      })
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("PNG");
  });
});
