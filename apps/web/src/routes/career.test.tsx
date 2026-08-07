import { screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { CompanyProfile } from "../companies/client";
import { renderWithProviders } from "../test/render";
import { CareerRoute } from "./career";

vi.mock("../companies/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../companies/client")>();
  return { ...actual, getCompanyBySlug: vi.fn() };
});
vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return { ...actual, searchJobs: vi.fn() };
});

const companies = await import("../companies/client");
const getCompanyBySlug = vi.mocked(companies.getCompanyBySlug);
const jobs = await import("../jobs/client");
const searchJobs = vi.mocked(jobs.searchJobs);

function profile(overrides: Partial<CompanyProfile> = {}): CompanyProfile {
  return {
    tenant_id: "t1",
    slug: "muster",
    display_name: "Muster",
    about: "Wer wir sind.",
    website: "https://muster.example",
    locations: ["Berlin"],
    benefits: ["Homeoffice"],
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getCompanyBySlug.mockResolvedValue(profile());
  searchJobs.mockResolvedValue({ ok: true, items: [], nextCursor: null });
});

describe("CareerRoute", () => {
  it("shows the company without any login", async () => {
    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByRole("heading", { name: "Muster" })).toBeInTheDocument();
    expect(screen.getByText("Wer wir sind.")).toBeInTheDocument();
  });

  it("asks for that company's jobs, not for all of them", async () => {
    renderWithProviders(<CareerRoute slug="muster" />);

    await waitFor(() => expect(searchJobs).toHaveBeenCalled());
    expect(searchJobs.mock.calls[0]?.[0]).toMatchObject({ company: "t1" });
  });

  it("says an unknown address is unknown, rather than showing an empty frame", async () => {
    getCompanyBySlug.mockResolvedValue(null);

    renderWithProviders(<CareerRoute slug="gibtsnicht" />);

    expect(await screen.findByText(/Diese Seite gibt es nicht/i)).toBeInTheDocument();
    expect(searchJobs).not.toHaveBeenCalled();
  });

  it("says plainly when nothing is advertised", async () => {
    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByText(/nichts ausgeschrieben/i)).toBeInTheDocument();
  });

  it("points at the one place where applying happens", async () => {
    // Ein zweiter Bewerbungsweg wäre ein zweiter Ort, an dem die Freigabe
    // entsteht.
    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByText(/dort entsteht die Freigabe/i)).toBeInTheDocument();
  });
});
