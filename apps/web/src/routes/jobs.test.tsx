import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Job } from "../jobs/client";
import { renderWithProviders } from "../test/render";
import { JobsRoute } from "./jobs";

vi.mock("../companies/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../companies/client")>();
  return { ...actual, getCompanyProfile: vi.fn() };
});
vi.mock("../applications/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../applications/client")>();
  return { ...actual, apply: vi.fn() };
});
vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return { ...actual, searchJobs: vi.fn() };
});

const client = await import("../jobs/client");
const searchJobs = vi.mocked(client.searchJobs);
const applicationsClient = await import("../applications/client");
const apply = vi.mocked(applicationsClient.apply);
const companiesClient = await import("../companies/client");
const getCompanyProfile = vi.mocked(companiesClient.getCompanyProfile);

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: crypto.randomUUID(),
    tenant_id: "t",
    title: "Backend-Entwicklerin",
    description: "Was zu tun ist.",
    location: "Berlin",
    remote: "hybrid",
    employment: "full_time",
    status: "published",
    published_at: "2026-08-02T10:00:00Z",
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

function principal(): MeResponse {
  return { user_id: "u", email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

beforeEach(() => {
  vi.clearAllMocks();
  searchJobs.mockResolvedValue({ ok: true, items: [], nextCursor: null });
  getCompanyProfile.mockResolvedValue(null);
  apply.mockResolvedValue({
    ok: true,
    application: {
      id: "a",
      job_id: "j1",
      tenant_id: "t",
      subject_id: "u",
      message: "",
      shares_resume: true,
      shares_portfolio: false,
      status: "submitted",
      created_at: "2026-08-02T10:00:00Z",
      updated_at: "2026-08-02T10:00:00Z",
    },
  });
});

describe("JobsRoute", () => {
  it("searches without a login — no principal needed at all", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText("Backend-Entwicklerin")).toBeInTheDocument();
  });

  it("says an empty result means nothing matched, not that something broke", async () => {
    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/keine.*gefunden|nichts gefunden/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("passes the typed query to the server", async () => {
    const user = userEvent.setup();
    renderWithProviders(<JobsRoute />);

    await user.type(await screen.findByLabelText(/Suchbegriff/i), "python");
    await user.click(screen.getByRole("button", { name: /Suchen/i }));

    await waitFor(() =>
      expect(searchJobs).toHaveBeenLastCalledWith(expect.objectContaining({ q: "python" }))
    );
  });

  it("spells out what hybrid means instead of showing the raw value", async () => {
    // "hybrid" ist ein Wert im Vertrag, kein Satz für Menschen.
    searchJobs.mockResolvedValue({ ok: true, items: [job({ remote: "full" })], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/vollständig remote/i)).toBeInTheDocument();
    expect(screen.queryByText("full")).toBeNull();
  });

  it("says 'Ort nicht angegeben' rather than leaving a gap", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ location: "" })], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/nicht angegeben/i)).toBeInTheDocument();
  });

  it("loads the next page from the cursor and keeps the first", async () => {
    const user = userEvent.setup();
    searchJobs.mockResolvedValueOnce({
      ok: true,
      items: [job({ title: "Erste" })],
      nextCursor: "cursor-1",
    });
    searchJobs.mockResolvedValueOnce({
      ok: true,
      items: [job({ title: "Zweite" })],
      nextCursor: null,
    });

    renderWithProviders(<JobsRoute />);

    await user.click(await screen.findByRole("button", { name: /Mehr laden/i }));

    await waitFor(() =>
      expect(searchJobs).toHaveBeenLastCalledWith(expect.objectContaining({ cursor: "cursor-1" }))
    );
    expect(await screen.findByText("Zweite")).toBeInTheDocument();
    expect(screen.getByText("Erste")).toBeInTheDocument();
  });

  it("reports a failed search instead of showing an empty list", async () => {
    // Eine leere Liste wäre die Behauptung, es gebe nichts.
    searchJobs.mockResolvedValue({ ok: false, message: "Die Suche ist fehlgeschlagen." });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByRole("alert")).toHaveTextContent("fehlgeschlagen");
    expect(screen.queryByText(/nichts gefunden/i)).toBeNull();
  });
});

describe("JobsRoute — bewerben", () => {
  it("offers applying only to someone who is logged in", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByRole("button", { name: /^Bewerben$/ })).toBeNull();
    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });

  it("does not offer a checkbox for the profile — it is not a choice", async () => {
    const user = userEvent.setup();
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });

    renderWithProviders(<JobsRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Bewerben$/ }));
    expect(screen.getByLabelText(/Lebenslauf/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Profil$/i)).toBeNull();
  });

  it("sends what was ticked", async () => {
    const user = userEvent.setup();
    searchJobs.mockResolvedValue({ ok: true, items: [job({ id: "j1" })], nextCursor: null });
    renderWithProviders(<JobsRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Bewerben$/ }));
    await user.click(screen.getByLabelText(/Meine Arbeiten/i));
    await user.click(screen.getByRole("button", { name: /Bewerbung abschicken/i }));

    await waitFor(() => expect(apply).toHaveBeenCalled());
    expect(apply.mock.calls[0]?.[0]).toMatchObject({
      job_id: "j1",
      shares_resume: true,
      shares_portfolio: true,
    });
  });

  it("says how to undo it, right where it was done", async () => {
    // Die Freigabe ist der Punkt; wo man sie zurücknimmt, gehört daneben.
    const user = userEvent.setup();
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });
    renderWithProviders(<JobsRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Bewerben$/ }));
    await user.click(screen.getByRole("button", { name: /Bewerbung abschicken/i }));

    expect(await screen.findByText(/Zurückziehen kannst du sie jederzeit/i)).toBeInTheDocument();
  });

  it("does not call a silent dependency a rejection", async () => {
    const user = userEvent.setup();
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });
    apply.mockResolvedValue({
      ok: false,
      reason: "unavailable",
      message: "Ein beteiligter Dienst antwortet gerade nicht.",
    });
    renderWithProviders(<JobsRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /^Bewerben$/ }));
    await user.click(screen.getByRole("button", { name: /Bewerbung abschicken/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("antwortet gerade nicht");
    expect(alert.textContent).not.toMatch(/abgelehnt/i);
  });
});

describe("JobsRoute — wer sucht", () => {
  it("names the company on the card", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ tenant_id: "t1" })], nextCursor: null });
    getCompanyProfile.mockResolvedValue({
      tenant_id: "t1",
      slug: "muster",
      display_name: "Muster",
      about: "",
      website: "https://muster.example",
      locations: [],
      benefits: ["Homeoffice"],
      updated_at: "2026-08-02T10:00:00Z",
    });

    renderWithProviders(<JobsRoute principal={null} />);

    expect(await screen.findByText("Muster")).toBeInTheDocument();
    expect(screen.getByText(/Homeoffice/)).toBeInTheDocument();
  });

  it("shows nothing rather than a placeholder when there is no profile", async () => {
    // „Unbekanntes Unternehmen" wäre eine Aussage, die niemand gemacht hat.
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });
    getCompanyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByText(/unbekannt/i)).toBeNull();
  });

  it("asks once per company, not once per job", async () => {
    // Der Query-Key hängt am Unternehmen; mehrere Stellen desselben
    // Arbeitgebers teilen sich eine Abfrage.
    searchJobs.mockResolvedValue({
      ok: true,
      items: [job({ id: "a", tenant_id: "t1" }), job({ id: "b", tenant_id: "t1" })],
      nextCursor: null,
    });
    getCompanyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findAllByText("Backend-Entwicklerin");
    await waitFor(() => expect(getCompanyProfile).toHaveBeenCalled());
    expect(getCompanyProfile).toHaveBeenCalledTimes(1);
  });
});
