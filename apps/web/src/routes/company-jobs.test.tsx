import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Job } from "../jobs/client";
import { renderWithProviders } from "../test/render";
import { CompanyJobsRoute } from "./company-jobs";

vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return {
    ...actual,
    listOwnJobs: vi.fn(),
    createJob: vi.fn(),
    publishJob: vi.fn(),
    closeJob: vi.fn(),
    draftJobText: vi.fn(),
  };
});

const client = await import("../jobs/client");
const listOwnJobs = vi.mocked(client.listOwnJobs);
const createJob = vi.mocked(client.createJob);
const publishJob = vi.mocked(client.publishJob);
const closeJob = vi.mocked(client.closeJob);
const draftJobText = vi.mocked(client.draftJobText);

const TENANT = "22222222-2222-2222-2222-222222222222";
const JOB = "11111111-1111-1111-1111-111111111111";

function principal(tenantId: string | null): MeResponse {
  return { user_id: "u", email: "chef@firma.example", tenant_id: tenantId, roles: ["user"] };
}

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: JOB,
    tenant_id: TENANT,
    title: "Backend-Entwicklerin",
    description: "Was zu tun ist.",
    location: "Berlin",
    remote: "hybrid",
    employment: "full_time",
    skills: [],
    status: "draft",
    published_at: null,
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listOwnJobs.mockResolvedValue({ ok: true, jobs: [] });
  createJob.mockResolvedValue({ ok: true, job: job() });
  publishJob.mockResolvedValue({ ok: true, job: job({ status: "published" }) });
  closeJob.mockResolvedValue({ ok: true, job: job({ status: "closed" }) });
  draftJobText.mockResolvedValue({ ok: true, draft: "Wir suchen jemanden für unser Backend." });
});

describe("CompanyJobsRoute", () => {
  it("asks for a company instead of showing an empty page", async () => {
    renderWithProviders(<CompanyJobsRoute principal={principal(null)} />);

    expect(await screen.findByText(/Unternehmen/i)).toBeInTheDocument();
    expect(listOwnJobs).not.toHaveBeenCalled();
  });

  it("shows drafts and closed ones too — they are the company's own", async () => {
    listOwnJobs.mockResolvedValue({
      ok: true,
      jobs: [job({ title: "Entwurf" }), job({ id: "2", title: "Zu", status: "closed" })],
    });

    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    expect(await screen.findByText("Entwurf")).toBeInTheDocument();
    expect(screen.getByText("Zu")).toBeInTheDocument();
  });

  it("offers publishing only for a draft", async () => {
    listOwnJobs.mockResolvedValue({ ok: true, jobs: [job({ status: "published" })] });

    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByRole("button", { name: /Veröffentlichen/i })).toBeNull();
    expect(screen.getByRole("button", { name: /Schließen/i })).toBeInTheDocument();
  });

  it("offers nothing at all for a closed one — there is no way back", async () => {
    listOwnJobs.mockResolvedValue({ ok: true, jobs: [job({ status: "closed" })] });

    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByRole("button", { name: /Veröffentlichen/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Schließen/i })).toBeNull();
  });

  it("creates a draft, never a published job", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Neue Stelle");
    await user.type(screen.getByLabelText(/Beschreibung/i), "Was zu tun ist.");
    await user.click(screen.getByRole("button", { name: /Entwurf anlegen/i }));

    await waitFor(() => expect(createJob).toHaveBeenCalled());
    // Veröffentlicht wird bewusst in einem zweiten Schritt.
    expect(publishJob).not.toHaveBeenCalled();
  });

  it("sends the required skills as a list, not as a line of text", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Neue Stelle");
    await user.type(screen.getByLabelText(/Beschreibung/i), "Was zu tun ist.");
    await user.type(screen.getByLabelText(/Fähigkeiten/i), "Python, Kubernetes ,, Go");
    await user.click(screen.getByRole("button", { name: /Entwurf anlegen/i }));

    await waitFor(() => expect(createJob).toHaveBeenCalled());
    // Getrennt und getrimmt, Leeres weg — derselbe Zerleger wie im Profil,
    // sonst verglichen sich später Sätze mit Wörtern.
    expect(createJob.mock.calls[0]?.[0].skills).toEqual(["Python", "Kubernetes", "Go"]);
  });

  it("says a wrong state differently than a wrong form", async () => {
    const user = userEvent.setup();
    listOwnJobs.mockResolvedValue({ ok: true, jobs: [job()] });
    publishJob.mockResolvedValue({
      ok: false,
      reason: "conflict",
      message: "A closed job cannot become published",
    });
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Veröffentlichen/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("closed job");
  });
});

describe("CompanyJobsRoute — Formulierungshilfe für die eigene Anzeige", () => {
  it("asks for nothing until somebody presses the button", async () => {
    // Derselbe Grundsatz wie beim Profil: kein Vorschlag von selbst. Nur
    // richtet er sich hier auf einen Text, den das Unternehmen selbst
    // geschrieben hat.
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await screen.findByLabelText(/Titel/i);
    expect(draftJobText).not.toHaveBeenCalled();
  });

  it("says at the button what leaves the platform — and that it is nobody's data", async () => {
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    const hint = await screen.findByText(/gehen dafür an Anthropic/i);
    // Der Unterschied zum Profil-Agenten, ausgeschrieben: hier geht nichts
    // über Menschen hinaus, sondern der eigene Anzeigentext.
    expect(hint.textContent).toMatch(/nichts über Bewerbende/i);
    expect(hint.textContent).toMatch(/Anforderungen erfindet der Vorschlag keine dazu/i);
  });

  it("sends the advert as it stands in the form, skills as a list", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Titel/i), "Backend-Entwicklerin");
    await user.type(screen.getByLabelText(/Fähigkeiten/i), "Python, Go");
    await user.click(screen.getByRole("button", { name: /Vorschlag holen/i }));

    await waitFor(() => expect(draftJobText).toHaveBeenCalled());
    const sent = draftJobText.mock.calls[0]?.[0];
    expect(sent?.title).toBe("Backend-Entwicklerin");
    expect(sent?.skills).toEqual(["Python", "Go"]);
    // Was nicht in der Nutzlast steht, kann nicht hinausgehen: keine
    // tenant_id, kein Firmenname, nichts über eine Person.
    expect(Object.keys(sent ?? {}).sort()).toEqual([
      "description",
      "location",
      "skills",
      "title",
      "wish",
    ]);
  });

  it("puts the draft into the description and creates nothing on its own", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Vorschlag holen/i }));

    expect(
      await screen.findByDisplayValue("Wir suchen jemanden für unser Backend.")
    ).toBeInTheDocument();
    // Der Entwurf ist ein Vorschlag, kein Ergebnis — angelegt wird die Stelle
    // erst, wenn jemand „Entwurf anlegen" drückt.
    expect(createJob).not.toHaveBeenCalled();
  });

  it("is not the submit button of the form it stands in", async () => {
    // Der Knopf sitzt im selben <form> wie „Entwurf anlegen". `Button` gibt
    // type="button" vor, aber genau deshalb steht es hier: ändert sich die
    // Vorgabe im UI-Paket, legte ein Klick auf „Vorschlag holen" sonst die
    // Stelle an — mit einer Beschreibung, die noch niemand gelesen hat.
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    const help = await screen.findByRole("button", { name: /Vorschlag holen/i });
    expect(help).toHaveAttribute("type", "button");
    expect(screen.getByRole("button", { name: /Entwurf anlegen/i })).toHaveAttribute(
      "type",
      "submit"
    );
  });

  it("warns before it overwrites a description that is already written", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Beschreibung/i), "Unser eigener Text.");

    expect(
      screen.getByRole("button", { name: /ersetzt die Beschreibung/i })
    ).toBeInTheDocument();
  });

  it("keeps the text when the provider is silent", async () => {
    const user = userEvent.setup();
    draftJobText.mockResolvedValue({
      ok: false,
      message: "Die Formulierungshilfe ist gerade nicht verfügbar. Dein Text bleibt unverändert.",
    });
    renderWithProviders(<CompanyJobsRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/Beschreibung/i), "Unser eigener Text.");
    await user.click(screen.getByRole("button", { name: /Vorschlag holen/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/nicht verfügbar/i);
    expect(screen.getByDisplayValue("Unser eigener Text.")).toBeInTheDocument();
  });
});
