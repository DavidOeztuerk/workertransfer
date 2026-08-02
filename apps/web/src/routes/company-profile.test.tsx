import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { CompanyProfile } from "../companies/client";
import { renderWithProviders } from "../test/render";
import { CompanyProfileRoute } from "./company-profile";

vi.mock("../companies/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../companies/client")>();
  return { ...actual, getOwnCompanyProfile: vi.fn(), saveCompanyProfile: vi.fn() };
});

const client = await import("../companies/client");
const getOwnCompanyProfile = vi.mocked(client.getOwnCompanyProfile);
const saveCompanyProfile = vi.mocked(client.saveCompanyProfile);

const TENANT = "11111111-1111-1111-1111-111111111111";

function principal(tenantId: string | null): MeResponse {
  return { user_id: "u", email: "chef@firma.example", tenant_id: tenantId, roles: ["user"] };
}

function profile(): CompanyProfile {
  return {
    tenant_id: TENANT,
    display_name: "Muster",
    about: "Wer wir sind.",
    website: "https://muster.example",
    locations: ["Berlin", "Hamburg"],
    benefits: ["Homeoffice"],
    updated_at: "2026-08-02T10:00:00Z",
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getOwnCompanyProfile.mockResolvedValue(null);
  saveCompanyProfile.mockResolvedValue({ ok: true, profile: profile() });
});

describe("CompanyProfileRoute", () => {
  it("asks for a company instead of showing an empty form", async () => {
    renderWithProviders(<CompanyProfileRoute principal={principal(null)} />);

    expect(await screen.findByText(/Wähle oben ein Unternehmen/i)).toBeInTheDocument();
    expect(getOwnCompanyProfile).not.toHaveBeenCalled();
  });

  it("fills the form with what is already stored", async () => {
    getOwnCompanyProfile.mockResolvedValue(profile());

    renderWithProviders(<CompanyProfileRoute principal={principal(TENANT)} />);

    expect(await screen.findByDisplayValue("Muster")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Berlin, Hamburg")).toBeInTheDocument();
  });

  it("splits the lists on commas and drops the blanks", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyProfileRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Anzeigename/i), "Muster");
    await user.type(screen.getByLabelText(/Standorte/i), "Berlin, , Hamburg ,");
    await user.click(screen.getByRole("button", { name: /Speichern/i }));

    await waitFor(() => expect(saveCompanyProfile).toHaveBeenCalled());
    expect(saveCompanyProfile.mock.calls[0]?.[0].locations).toEqual(["Berlin", "Hamburg"]);
  });

  it("sends an empty website as null, not as an empty string", async () => {
    // Ein leerer String würde gerendert und führte ins Nichts.
    const user = userEvent.setup();
    renderWithProviders(<CompanyProfileRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Anzeigename/i), "Muster");
    await user.click(screen.getByRole("button", { name: /Speichern/i }));

    await waitFor(() => expect(saveCompanyProfile).toHaveBeenCalled());
    expect(saveCompanyProfile.mock.calls[0]?.[0].website).toBeNull();
  });

  it("says plainly what happens while nothing is filled in", async () => {
    renderWithProviders(<CompanyProfileRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/bleibt eine Ausschreibung anonym/i)).toBeInTheDocument();
  });

  it("keeps a rejected form on screen and does not claim success", async () => {
    const user = userEvent.setup();
    getOwnCompanyProfile.mockResolvedValue(profile());
    saveCompanyProfile.mockResolvedValue({
      ok: false,
      reason: "invalid",
      message: "Only http and https links are allowed",
    });
    renderWithProviders(<CompanyProfileRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Speichern/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("http and https");
    expect(screen.queryByText(/gespeichert/i)).toBeNull();
  });
});
