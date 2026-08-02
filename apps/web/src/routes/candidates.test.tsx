import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Profile } from "../profile/client";
import { renderWithProviders } from "../test/render";
import { CandidatesRoute } from "./candidates";

vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return { ...actual, listCandidates: vi.fn() };
});

const client = await import("../profile/client");
const listCandidates = vi.mocked(client.listCandidates);

const TENANT = "22222222-2222-2222-2222-222222222222";

function principal(tenantId: string | null): MeResponse {
  return { user_id: "u", email: "anna@firma.de", tenant_id: tenantId, roles: ["user"] };
}

function candidate(id: string, headline: string, extra: Partial<Profile> = {}): Profile {
  return {
    subject_id: id,
    headline,
    bio: "",
    location: "Berlin",
    remote_ok: false,
    skills: ["Python"],
    updated_at: "2026-08-02T10:00:00Z",
    ...extra,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listCandidates.mockResolvedValue({ ok: true, items: [], nextCursor: null });
});

describe("CandidatesRoute", () => {
  it("does not even ask without an active company", async () => {
    renderWithProviders(<CandidatesRoute principal={principal(null)} />);

    expect(await screen.findByText(/Unternehmen/i)).toBeInTheDocument();
    expect(listCandidates).not.toHaveBeenCalled();
  });

  it("lists what the ledger released", async () => {
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python"), candidate("b", "Data Engineer")],
      nextCursor: null,
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByText("Senior Python")).toBeInTheDocument();
    expect(screen.getByText("Data Engineer")).toBeInTheDocument();
  });

  it("says an empty list means nobody released — not that something broke", async () => {
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/freigegeben/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("shows nothing at all while the ledger is silent", async () => {
    // Eine leere Liste wäre hier eine Behauptung über Menschen, die niemand
    // gerade belegen kann.
    listCandidates.mockResolvedValue({
      ok: false,
      reason: "consent-unavailable",
      message: "Der Consent-Ledger antwortet gerade nicht.",
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Consent-Ledger");
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("loads the next page from the cursor instead of starting over", async () => {
    const user = userEvent.setup();
    listCandidates.mockResolvedValueOnce({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: "cursor-1",
    });
    listCandidates.mockResolvedValueOnce({
      ok: true,
      items: [candidate("b", "Data Engineer")],
      nextCursor: null,
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Mehr laden/i }));

    await waitFor(() => expect(listCandidates).toHaveBeenLastCalledWith("cursor-1"));
    expect(await screen.findByText("Data Engineer")).toBeInTheDocument();
    // Die vorige Seite bleibt stehen — sonst wäre "mehr" ein Austausch.
    expect(screen.getByText("Senior Python")).toBeInTheDocument();
  });

  it("hides the button on the last page", async () => {
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: null,
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    await screen.findByText("Senior Python");
    expect(screen.queryByRole("button", { name: /Mehr laden/i })).toBeNull();
  });

  it("never promises a total — the count says nothing about who is hidden", async () => {
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: "cursor-1",
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    await screen.findByText("Senior Python");
    expect(screen.queryByText(/von \d+/)).toBeNull();
    expect(screen.queryByText(/insgesamt/i)).toBeNull();
  });
});
