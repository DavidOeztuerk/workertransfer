import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { Application } from "../applications/client";
import type { MeResponse } from "../auth/client";
import { renderWithProviders } from "../test/render";
import { ApplicationsRoute } from "./applications";

vi.mock("../applications/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../applications/client")>();
  return { ...actual, listMyApplications: vi.fn(), withdrawApplication: vi.fn() };
});

const client = await import("../applications/client");
const listMyApplications = vi.mocked(client.listMyApplications);
const withdrawApplication = vi.mocked(client.withdrawApplication);

function principal(): MeResponse {
  return { user_id: "u", email: "anna@example.com", tenant_id: null };
}

function application(overrides: Partial<Application> = {}): Application {
  return {
    id: "a1",
    job_id: "j1",
    tenant_id: "t1",
    subject_id: "u",
    message: "",
    shares_resume: true,
    shares_portfolio: false,
    status: "submitted",
    created_at: "2026-08-02T10:00:00Z",
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listMyApplications.mockResolvedValue({ ok: true, applications: [] });
  withdrawApplication.mockResolvedValue({
    ok: true,
    application: application({ status: "withdrawn" }),
  });
});

describe("ApplicationsRoute", () => {
  it("names what is currently shared", async () => {
    listMyApplications.mockResolvedValue({
      ok: true,
      applications: [application({ shares_portfolio: true })],
    });

    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    expect(await screen.findByText(/Profil, Lebenslauf, Arbeiten/)).toBeInTheDocument();
  });

  it("offers withdrawing only while something is actually shared", async () => {
    // Ein Knopf für eine Bewerbung, die schon zu ist, wäre eine Lüge über den
    // Zustand.
    listMyApplications.mockResolvedValue({
      ok: true,
      applications: [application({ status: "rejected" })],
    });

    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    await screen.findByText("Abgelehnt");
    expect(screen.queryByRole("button", { name: /Zurückziehen/i })).toBeNull();
  });

  it("still offers it while the company is reading", async () => {
    // Wer nicht mehr will, muss nicht warten, bis jemand anderes fertig ist.
    listMyApplications.mockResolvedValue({
      ok: true,
      applications: [application({ status: "reviewing" })],
    });

    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    expect(await screen.findByRole("button", { name: /Zurückziehen/i })).toBeInTheDocument();
  });

  it("withdraws through the server", async () => {
    const user = userEvent.setup();
    listMyApplications.mockResolvedValue({ ok: true, applications: [application()] });
    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Zurückziehen/i }));

    await waitFor(() => expect(withdrawApplication).toHaveBeenCalledWith("a1"));
  });

  it("says plainly that a withdrawn application closed the access", async () => {
    listMyApplications.mockResolvedValue({
      ok: true,
      applications: [application({ status: "withdrawn" })],
    });

    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    expect(await screen.findByText(/sieht deine Daten nicht mehr/i)).toBeInTheDocument();
  });


  // Vorher sah man hier eine LEERE KARTE: `result` war undefined, also griff
  // keiner der drei Zweige, und es stand nichts da — kein Ladehinweis, kein
  // Leerzustand, nichts. Wer eine langsame Verbindung hat, konnte nicht
  // unterscheiden, ob geladen wird oder ob er keine Bewerbungen hat.
  it("says it is loading instead of showing an empty card", async () => {
    let loesen: (wert: { ok: true; applications: [] }) => void = () => {};
    listMyApplications.mockReturnValue(
      new Promise((resolve) => {
        loesen = resolve;
      })
    );

    renderWithProviders(<ApplicationsRoute principal={principal()} />);

    expect(screen.getByRole("status")).toHaveTextContent("Bewerbungen werden geladen…");
    // Und ausdrücklich NICHT der Leerzustand: „noch keine Bewerbung" wäre
    // während des Ladens eine Behauptung, die niemand kennt.
    expect(screen.queryByText(/Noch keine Bewerbung/)).toBeNull();

    loesen({ ok: true, applications: [] });
    expect(await screen.findByText(/Noch keine Bewerbung/)).toBeInTheDocument();
  });

  it("tells an anonymous visitor to log in", () => {
    renderWithProviders(<ApplicationsRoute principal={null} />);

    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
    expect(listMyApplications).not.toHaveBeenCalled();
  });
});
