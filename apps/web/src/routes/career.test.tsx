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


  // Vorher log die Seite hier: `items` wurde bei einem gescheiterten Abruf
  // GENAUSO leer wie bei einem leeren Ergebnis, und die Seite sagte „Zurzeit ist
  // nichts ausgeschrieben". Ein Unternehmen, dessen Stellen gerade nicht
  // abrufbar sind, sah damit aus wie eines, das keine hat — die beruhigendste
  // falsche Antwort, die es gibt.
  it("does not pass a failed fetch off as an empty list", async () => {
    searchJobs.mockResolvedValue({ ok: false, message: "jobs-service antwortet nicht" });

    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByRole("alert")).toHaveTextContent("nicht abrufbar");
    expect(screen.queryByText(/nichts ausgeschrieben/i)).toBeNull();
  });

  it("sagt, dass mit dem Bewerben die Freigabe entsteht — und für wen", async () => {
    // Der Satz stand vorher hier, weil diese Seite auf die Stellensuche
    // verwies. Der Verweis ist weg, die Aussage bleibt: sie ist der Grund,
    // warum überhaupt jemand auf den Knopf drückt.
    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByText(/entsteht die Freigabe deiner Daten/i)).toBeInTheDocument();
    expect(screen.getByText(/nur für dieses eine Unternehmen/i)).toBeInTheDocument();
  });

  it("führt je Stelle direkt auf die eine Bewerbungsseite", async () => {
    // „Ein Ort, an dem die Freigabe entsteht" gilt strenger als vorher: früher
    // verwies diese Seite auf die Stellensuche, und DIE trug ein eigenes
    // Formular. Jetzt zeigen beide Listen auf dieselbe Adresse — und jemandem,
    // der die Stelle vor sich hat, wird nicht gesagt, er solle sie noch einmal
    // suchen.
    searchJobs.mockResolvedValue({
      ok: true,
      items: [
        {
          id: "11111111-2222-3333-4444-555555555555",
          tenant_id: "t1",
          title: "Backend-Entwicklerin",
          description: "Was zu tun ist.",
          location: "Berlin",
          remote: "hybrid",
          employment: "full_time",
          skills: [],
          status: "published",
          published_at: "2026-08-02T10:00:00Z",
          updated_at: "2026-08-02T10:00:00Z",
        },
      ],
      nextCursor: null,
    });

    renderWithProviders(<CareerRoute slug="muster" />);

    expect(await screen.findByRole("link", { name: /^Bewerben$/ })).toHaveAttribute(
      "href",
      "/jobs/11111111-2222-3333-4444-555555555555/apply"
    );
  });
});
