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
vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return { ...actual, getMyProfile: vi.fn() };
});

const client = await import("../jobs/client");
const searchJobs = vi.mocked(client.searchJobs);
const profileClient = await import("../profile/client");
const getMyProfile = vi.mocked(profileClient.getMyProfile);

function myProfile(skills: string[]) {
  return {
    subject_id: "u",
    headline: "Entwicklerin",
    bio: "",
    location: "Berlin",
    remote_ok: true,
    skills,
    updated_at: "2026-08-02T10:00:00Z",
  };
}
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
    skills: [],
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
  getMyProfile.mockResolvedValue(null);
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

describe("JobsRoute — Passung", () => {
  const WITH_SKILLS = { skills: ["Python", "Kubernetes", "Go"] };

  it("names what you have and what you lack — and no percentage anywhere", async () => {
    // Eine Prozentzahl sieht aus wie eine Messung und ist eine Division. Sie
    // verschweigt genau das, was zählt: WELCHE Fähigkeit fehlt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["python", "Kubernetes"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/2 von 3/)).toBeInTheDocument();
    expect(screen.getByText(/^Go$/)).toBeInTheDocument();
    expect(screen.queryByText(/%/)).toBeNull();
  });

  it("marks the missing one as missing, not merely as absent from the list", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    // Der fehlende Teil ist der, der etwas nützt: er sagt, was man tun könnte.
    const missing = await screen.findByText(/^Go$/);
    expect(missing).toHaveAttribute("data-match", "missing");
    expect(screen.getByText(/^Python$/)).toHaveAttribute("data-match", "have");
  });

  it("asks nothing about a person who is not logged in", async () => {
    // Ohne Anmeldung gibt es kein Profil, also nichts zu vergleichen — die
    // Fähigkeiten stehen trotzdem da, sie gehören zur Ausschreibung.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });

    renderWithProviders(<JobsRoute principal={null} />);

    expect(await screen.findByText(/^Go$/)).toBeInTheDocument();
    expect(screen.queryByText(/von 3/)).toBeNull();
    expect(getMyProfile).not.toHaveBeenCalled();
  });

  it("says 'nichts eingetragen' instead of '0 von 3'", async () => {
    // „0 von 3" wäre eine Aussage über den Menschen, die nicht stimmt: er hat
    // nichts gesagt, nicht nichts gekonnt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile([]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/Profil/i)).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).toBeNull();
  });

  it("points at the profile of someone who has none at all, not just an empty one", async () => {
    // `getMyProfile()` liefert `null` für „noch keins angelegt". Ohne diesen
    // Fall bliebe die Seite genau dort stumm, wo ein Satz die ganze Funktion
    // erklärt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/Trage Fähigkeiten in deinem/i)).toBeInTheDocument();
    expect(screen.queryByText(/von 3/)).toBeNull();
  });

  it("shows no skill line at all when the job names none", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ skills: [] })], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByText(/von 0/)).toBeNull();
    expect(screen.queryByText(/genannten Fähigkeiten/)).toBeNull();
  });

  it("asks for the profile once, not once per job", async () => {
    searchJobs.mockResolvedValue({
      ok: true,
      items: [job({ id: "a", ...WITH_SKILLS }), job({ id: "b", ...WITH_SKILLS })],
      nextCursor: null,
    });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    await screen.findAllByText("Backend-Entwicklerin");
    await waitFor(() => expect(getMyProfile).toHaveBeenCalled());
    expect(getMyProfile).toHaveBeenCalledTimes(1);
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
