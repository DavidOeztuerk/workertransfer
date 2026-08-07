import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import { renderWithProviders } from "../test/render";
import { ConsentsRoute } from "./consents";

vi.mock("../consent/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../consent/client")>();
  return { ...actual, listMyConsents: vi.fn(), setGranted: vi.fn() };
});
vi.mock("../companies/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../companies/client")>();
  return { ...actual, getCompanyProfile: vi.fn() };
});

const consentClient = await import("../consent/client");
const listMyConsents = vi.mocked(consentClient.listMyConsents);
const setGranted = vi.mocked(consentClient.setGranted);
const companiesClient = await import("../companies/client");
const getCompanyProfile = vi.mocked(companiesClient.getCompanyProfile);

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TENANT = "22222222-2222-2222-2222-222222222222";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function granted(capability: string) {
  return { capability, granted_at: "2026-08-02T10:00:00Z" };
}

beforeEach(() => {
  vi.clearAllMocks();
  listMyConsents.mockResolvedValue({ ok: true, consents: [] });
  setGranted.mockResolvedValue({ ok: true, granted: false });
  getCompanyProfile.mockResolvedValue(null);
});

describe("ConsentsRoute", () => {
  it("asks for a login rather than showing consents nobody owns", async () => {
    renderWithProviders(<ConsentsRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(listMyConsents).not.toHaveBeenCalled();
  });

  it("says plainly that nothing is shared when nothing is", async () => {
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByText(/Niemand sieht etwas von dir/)).toBeTruthy();
  });

  it("never shows an error as an empty list", async () => {
    // „Du hast nichts freigegeben" wäre hier die beruhigendste falsche Antwort,
    // die dieses System geben kann.
    listMyConsents.mockResolvedValue({ ok: false, message: "Keine Verbindung." });
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText(/Niemand sieht etwas von dir/)).toBeNull();
  });

  it("names the area and says a public grant covers every company", async () => {
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted("profile.visibility:public")],
    });
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByText(/Profil · Alle Unternehmen/)).toBeTruthy();
  });

  it("resolves the company name instead of showing a UUID", async () => {
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted(`resume.visibility:tenant:${TENANT}`)],
    });
    getCompanyProfile.mockResolvedValue({
      tenant_id: TENANT,
      display_name: "Acme GmbH",
      about: "",
      website: "",
      locations: [],
      benefits: [],
      slug: "acme",
      updated_at: "2026-08-02T10:00:00Z",
    });
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByText(/Lebenslauf · Acme GmbH/)).toBeTruthy();
    expect(screen.queryByText(new RegExp(TENANT))).toBeNull();
  });

  it("does not invent a name for a company without a profile", async () => {
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted(`market.visibility:tenant:${TENANT}`)],
    });
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByText(/Marktstatus · Ein Unternehmen/)).toBeTruthy();
  });

  it("shows an unknown capability instead of swallowing it", async () => {
    // Eine Freigabe zu verbergen, weil die Oberfläche ihr Format nicht kennt,
    // wäre auf genau dieser Seite der schlimmste denkbare Fehler.
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted("something.entirely:new")],
    });
    renderWithProviders(<ConsentsRoute principal={principal()} />);

    expect(await screen.findByText(/something.entirely:new/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Zurückziehen" })).toBeTruthy();
  });

  it("withdraws with a reason, because a withdrawal has to be explainable", async () => {
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted("profile.visibility:public")],
    });
    renderWithProviders(<ConsentsRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Zurückziehen" }));

    await waitFor(() =>
      expect(setGranted).toHaveBeenCalledWith(
        SUBJECT,
        "profile.visibility:public",
        false,
        expect.stringContaining("Freigaben")
      )
    );
  });

  it("reports a failed withdrawal instead of pretending it worked", async () => {
    listMyConsents.mockResolvedValue({
      ok: true,
      consents: [granted("profile.visibility:public")],
    });
    setGranted.mockResolvedValue({ ok: false, message: "Keine Verbindung zum Consent-Ledger." });
    renderWithProviders(<ConsentsRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Zurückziehen" }));

    expect(await screen.findByRole("alert")).toBeTruthy();
  });
});
