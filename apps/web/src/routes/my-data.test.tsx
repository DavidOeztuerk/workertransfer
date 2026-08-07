import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import { renderWithProviders } from "../test/render";
import { MyDataRoute } from "./my-data";

vi.mock("../auth/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/client")>();
  return { ...actual, fetchMe: vi.fn() };
});
vi.mock("../applications/client", async (o) => ({
  ...(await o<typeof import("../applications/client")>()),
  listMyApplications: vi.fn(),
}));
vi.mock("../consent/client", async (o) => ({
  ...(await o<typeof import("../consent/client")>()),
  listMyConsents: vi.fn(),
  listMyConsentHistory: vi.fn(),
}));
vi.mock("../market/client", async (o) => ({
  ...(await o<typeof import("../market/client")>()),
  listMyMarketRequests: vi.fn(),
  getMyMarketStatus: vi.fn(),
}));
vi.mock("../profile/client", async (o) => ({
  ...(await o<typeof import("../profile/client")>()),
  getMyProfile: vi.fn(),
}));
vi.mock("../portfolio/client", async (o) => ({
  ...(await o<typeof import("../portfolio/client")>()),
  getMyPortfolio: vi.fn(),
}));
vi.mock("../resume/client", async (o) => ({
  ...(await o<typeof import("../resume/client")>()),
  getMyResume: vi.fn(),
  listMyRequests: vi.fn(),
}));
vi.mock("../settings/client", async (o) => ({
  ...(await o<typeof import("../settings/client")>()),
  getNotificationPreferences: vi.fn(),
}));
vi.mock("../transfers/client", async (o) => ({
  ...(await o<typeof import("../transfers/client")>()),
  listMyTransfers: vi.fn(),
}));

const auth = await import("../auth/client");
const applications = await import("../applications/client");
const consent = await import("../consent/client");
const market = await import("../market/client");
const profile = await import("../profile/client");
const portfolio = await import("../portfolio/client");
const resume = await import("../resume/client");
const settings = await import("../settings/client");
const transfers = await import("../transfers/client");

const SUBJECT = "11111111-1111-1111-1111-111111111111";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(auth.fetchMe).mockResolvedValue(principal());
  vi.mocked(applications.listMyApplications).mockResolvedValue({ ok: true, applications: [] });
  vi.mocked(consent.listMyConsents).mockResolvedValue({ ok: true, consents: [] });
  vi.mocked(consent.listMyConsentHistory).mockResolvedValue({ ok: true, events: [] });
  vi.mocked(market.listMyMarketRequests).mockResolvedValue({ ok: true, requests: [] });
  vi.mocked(market.getMyMarketStatus).mockResolvedValue({
    subject_id: SUBJECT,
    availability: "unavailable",
    employed: false,
    note: "",
    is_approachable: false,
    updated_at: "2026-08-02T10:00:00Z",
  });
  vi.mocked(profile.getMyProfile).mockResolvedValue(null);
  vi.mocked(portfolio.getMyPortfolio).mockResolvedValue(null);
  vi.mocked(resume.getMyResume).mockResolvedValue(null);
  vi.mocked(resume.listMyRequests).mockResolvedValue({ ok: true, requests: [] });
  vi.mocked(settings.getNotificationPreferences).mockResolvedValue({
    resume_request: true,
    market_request: true,
    application_update: true,
    transfer_update: true,
  });
  vi.mocked(transfers.listMyTransfers).mockResolvedValue({ ok: true, transfers: [] });
});

describe("MyDataRoute", () => {
  it("asks for a login rather than collecting data for nobody", async () => {
    renderWithProviders(<MyDataRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(auth.fetchMe).not.toHaveBeenCalled();
  });

  it("lists every section, including the empty ones", async () => {
    // „Kein Lebenslauf" ist eine Auskunft. Sie fehlt sonst.
    renderWithProviders(<MyDataRoute principal={principal()} />);

    expect(await screen.findByText(/lebenslauf — enthalten/)).toBeTruthy();
    expect(screen.getByText(/portfolio — enthalten/)).toBeTruthy();
    expect(screen.getByText(/freigaben verlauf — enthalten/)).toBeTruthy();
  });

  it("warns before the download when a part is missing", async () => {
    // In der Datei steht es auch — aber wer sie herunterlädt, soll es vorher
    // wissen.
    vi.mocked(transfers.listMyTransfers).mockResolvedValue({ ok: false, message: "weg" });
    renderWithProviders(<MyDataRoute principal={principal()} />);

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("transfers");
    expect(screen.getByText(/transfers — fehlt/)).toBeTruthy();
  });

  it("keeps the consent history, which the overview page deliberately omits", async () => {
    vi.mocked(consent.listMyConsentHistory).mockResolvedValue({
      ok: true,
      events: [
        {
          capability: "profile.visibility:public",
          action: "REVOKE",
          recorded_at: "2026-08-02T10:00:00Z",
          reason: "doch nicht",
        },
      ],
    });
    renderWithProviders(<MyDataRoute principal={principal()} />);

    expect(await screen.findByText(/freigaben verlauf — enthalten/)).toBeTruthy();
  });

  it("points to deleting as a different path — a link, never a neighbouring button", async () => {
    // Der Weg existiert jetzt (ADR-0027), und die Karte darf ihn nicht länger
    // als „gibt es nicht" beschreiben. Aber er bleibt ein VERWEIS: ein
    // Löschknopf neben „Als JSON herunterladen" wäre genau die Nachbarschaft,
    // in der ein Fehlklick unwiderruflich wird.
    renderWithProviders(<MyDataRoute principal={principal()} />);

    const link = await screen.findByRole("link", { name: /Konto löschen/i });
    expect(link.getAttribute("href")).toBe("/konto-loeschen");
    expect(screen.queryByRole("button", { name: /löschen/i })).toBeNull();
  });
});
