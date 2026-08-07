import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { MarketRequest, MarketStatus } from "../market/client";
import { renderWithProviders } from "../test/render";
import { MarketRoute } from "./market";

vi.mock("../market/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../market/client")>();
  return {
    ...actual,
    getMyMarketStatus: vi.fn(),
    saveMyMarketStatus: vi.fn(),
    listMyMarketRequests: vi.fn(),
    answerMarketRequest: vi.fn(),
    revokeMarketAccess: vi.fn(),
  };
});

const client = await import("../market/client");
const getMyMarketStatus = vi.mocked(client.getMyMarketStatus);
const saveMyMarketStatus = vi.mocked(client.saveMyMarketStatus);
const listMyMarketRequests = vi.mocked(client.listMyMarketRequests);
const answerMarketRequest = vi.mocked(client.answerMarketRequest);
const revokeMarketAccess = vi.mocked(client.revokeMarketAccess);

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const REQUEST_ID = "22222222-2222-2222-2222-222222222222";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function status(overrides: Partial<MarketStatus> = {}): MarketStatus {
  return {
    subject_id: SUBJECT,
    availability: "unavailable",
    employed: false,
    note: "",
    is_approachable: false,
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

function request(overrides: Partial<MarketRequest> = {}): MarketRequest {
  return {
    id: REQUEST_ID,
    subject_id: SUBJECT,
    tenant_id: "33333333-3333-3333-3333-333333333333",
    status: "PENDING",
    created_at: "2026-08-02T10:00:00Z",
    answered_at: null,
    active: null,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyMarketStatus.mockResolvedValue(status());
  listMyMarketRequests.mockResolvedValue({ ok: true, requests: [] });
  saveMyMarketStatus.mockResolvedValue({ ok: true, status: status({ availability: "open" }) });
  answerMarketRequest.mockResolvedValue({ ok: true, request: request({ status: "GRANTED" }) });
  revokeMarketAccess.mockResolvedValue({ ok: true, request: request({ status: "GRANTED" }) });
});

describe("MarketRoute", () => {
  it("asks for a login rather than showing a status nobody owns", async () => {
    renderWithProviders(<MarketRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(getMyMarketStatus).not.toHaveBeenCalled();
  });

  it("preselects the loaded status — never a friendlier one", async () => {
    // Die Voreinstellung darf nie zugunsten des Marktes ausfallen: wer nichts
    // gesagt hat, hat nicht „ich höre zu" gesagt.
    renderWithProviders(<MarketRoute principal={principal()} />);

    const chosen = await screen.findByRole("radio", { name: /Gerade nicht/ });
    expect((chosen as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole("radio", { name: /Ich höre zu/ }) as HTMLInputElement).checked).toBe(
      false
    );
  });

  it("sends exactly what was chosen", async () => {
    renderWithProviders(<MarketRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("radio", { name: /Ich höre zu/ }));
    await user.click(screen.getByRole("checkbox", { name: /Ich arbeite gerade irgendwo/ }));
    await user.type(screen.getByLabelText(/Notiz/), "Backend, remote");
    await user.click(screen.getByRole("button", { name: "Speichern" }));

    await waitFor(() => {
      expect(saveMyMarketStatus).toHaveBeenCalledWith({
        availability: "listening",
        employed: true,
        note: "Backend, remote",
      });
    });
  });

  it("says that the platform will not contact the current employer", async () => {
    // Die zentrale Zusage dieses Systems. Sie steht dort, wo jemand „ich
    // arbeite gerade irgendwo" ankreuzt — nicht in einer Fußnote.
    renderWithProviders(<MarketRoute principal={principal()} />);

    expect(await screen.findByText(/Diese Plattform fragt ihn nicht/)).toBeTruthy();
  });

  it("offers granting and declining for an open request", async () => {
    listMyMarketRequests.mockResolvedValue({ ok: true, requests: [request()] });
    renderWithProviders(<MarketRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Freigeben" }));

    await waitFor(() => {
      expect(answerMarketRequest).toHaveBeenCalledWith(REQUEST_ID, true);
    });
  });

  it("offers a withdrawal only while the access actually holds", async () => {
    // `status` sagt, was geschehen ist; `active` sagt, was gilt. Ein Knopf für
    // eine Freigabe, die es nicht mehr gibt, wäre eine Lüge über den Zustand.
    listMyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [request({ status: "GRANTED", active: false })],
    });
    renderWithProviders(<MarketRoute principal={principal()} />);

    expect(await screen.findByText(/Freigabe zurückgezogen/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Zurückziehen" })).toBeNull();
  });

  it("withdraws a granted access", async () => {
    listMyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [request({ status: "GRANTED", active: true })],
    });
    renderWithProviders(<MarketRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Zurückziehen" }));

    await waitFor(() => {
      expect(revokeMarketAccess).toHaveBeenCalledWith(REQUEST_ID);
    });
  });

  it("shows a silent ledger as a problem, not as an empty list", async () => {
    listMyMarketRequests.mockResolvedValue({
      ok: false,
      message: "Der Consent-Ledger antwortet gerade nicht.",
    });
    renderWithProviders(<MarketRoute principal={principal()} />);

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("Bislang hat niemand gefragt.")).toBeNull();
  });

  it("reports a failed save instead of claiming success", async () => {
    saveMyMarketStatus.mockResolvedValue({ ok: false, message: "Keine Verbindung zum Server." });
    renderWithProviders(<MarketRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Speichern" }));

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("Marktstatus gespeichert.")).toBeNull();
  });
});
