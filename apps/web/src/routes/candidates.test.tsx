import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { MarketRequest, MarketStatus } from "../market/client";
import type { Profile } from "../profile/client";
import { renderWithProviders } from "../test/render";
import { CandidatesRoute } from "./candidates";

vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return { ...actual, listCandidates: vi.fn() };
});
vi.mock("../resume/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../resume/client")>();
  return { ...actual, requestResume: vi.fn() };
});
vi.mock("../market/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../market/client")>();
  return {
    ...actual,
    listCompanyMarketRequests: vi.fn(),
    requestMarketStatus: vi.fn(),
    getMarketStatus: vi.fn(),
  };
});
vi.mock("../transfers/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../transfers/client")>();
  return { ...actual, expressInterest: vi.fn() };
});

const client = await import("../profile/client");
const listCandidates = vi.mocked(client.listCandidates);
const resumeClient = await import("../resume/client");
const requestResume = vi.mocked(resumeClient.requestResume);
const marketClient = await import("../market/client");
const listCompanyMarketRequests = vi.mocked(marketClient.listCompanyMarketRequests);
const requestMarketStatus = vi.mocked(marketClient.requestMarketStatus);
const getMarketStatus = vi.mocked(marketClient.getMarketStatus);
const transfersClient = await import("../transfers/client");
const expressInterest = vi.mocked(transfersClient.expressInterest);

function marketRequest(
  subjectId: string,
  status: "PENDING" | "GRANTED" | "DECLINED"
): MarketRequest {
  return {
    id: `req-${subjectId}`,
    subject_id: subjectId,
    tenant_id: TENANT,
    status,
    created_at: "2026-08-02T10:00:00Z",
    answered_at: null,
    active: null,
  };
}

function marketStatus(
  overrides: Partial<MarketStatus> = {}
): MarketStatus {
  return {
    subject_id: "a",
    availability: "listening",
    employed: true,
    note: "",
    is_approachable: true,
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

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
  requestResume.mockResolvedValue({
    ok: true,
    request: {
      id: "r",
      subject_id: "a",
      tenant_id: TENANT,
      status: "PENDING",
      created_at: "2026-08-02T10:00:00Z",
    },
  });
  listCompanyMarketRequests.mockResolvedValue({ ok: true, requests: [] });
  requestMarketStatus.mockResolvedValue({ ok: true, request: marketRequest("a", "PENDING") });
  getMarketStatus.mockResolvedValue({ ok: true, status: marketStatus() });
  expressInterest.mockResolvedValue({
    ok: true,
    transfer: {
      id: "t",
      subject_id: "a",
      tenant_id: TENANT,
      status: "interested",
      requires_release: true,
      release_confirmed: false,
      message: "",
      offer_note: "",
      offer_start_on: null,
      offer_fee_cents: null,
      created_at: "2026-08-02T10:00:00Z",
      updated_at: "2026-08-02T10:00:00Z",
    },
  });
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

describe("CandidatesRoute — Lebenslauf anfragen", () => {
  it("offers a request per candidate", async () => {
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: null,
    });

    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(
      await screen.findByRole("button", { name: /Lebenslauf anfragen/i })
    ).toBeInTheDocument();
  });

  it("asks through the server with the subject id, nothing else", async () => {
    const user = userEvent.setup();
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: null,
    });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Lebenslauf anfragen/i }));

    await waitFor(() => expect(requestResume).toHaveBeenCalledWith("a"));
  });

  it("keeps a rejected request visible instead of pretending it worked", async () => {
    const user = userEvent.setup();
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: null,
    });
    requestResume.mockResolvedValue({
      ok: false,
      reason: "already-asked",
      message: "Ihr habt diese Person bereits gefragt.",
    });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Lebenslauf anfragen/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("bereits gefragt");
  });

  it("does not offer the request to someone without a company", async () => {
    renderWithProviders(<CandidatesRoute principal={principal(null)} />);

    await screen.findByText(/Unternehmen/i);
    expect(screen.queryByRole("button", { name: /Lebenslauf anfragen/i })).toBeNull();
  });
});

describe("the market status on a candidate card", () => {
  beforeEach(() => {
    listCandidates.mockResolvedValue({
      ok: true,
      items: [candidate("a", "Senior Python")],
      nextCursor: null,
    });
  });

  it("survives a session that arrives late", async () => {
    // Genau der Fall aus der Praxis: der erste Render hat noch kein
    // Unternehmen (die Sitzung lädt), der zweite hat eines. Stand ein Hook
    // hinter dem frühen Rückgabesprung, warf React beim zweiten Render
    // "Rendered more hooks than during the previous render" — und die ganze
    // Seite war weg. Die anderen Tests sehen das nie: sie rendern nur einmal.
    const view = renderWithProviders(<CandidatesRoute principal={principal(null)} />);
    expect(await screen.findByText(/Profile sehen nur Unternehmen/)).toBeInTheDocument();

    view.rerender(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByText("Senior Python")).toBeInTheDocument();
  });

  it("asks separately from the resume — one grant must not carry the other", async () => {
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Marktstatus anfragen" }));

    await waitFor(() => expect(requestMarketStatus).toHaveBeenCalledWith("a"));
    expect(requestResume).not.toHaveBeenCalled();
  });

  it("does not consult the ledger for a person who was never asked", async () => {
    // Sonst sähe der Ledger bei jedem Seitenaufruf eine Prüfung zu jeder Person.
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("button", { name: "Marktstatus anfragen" })).toBeInTheDocument();
    expect(getMarketStatus).not.toHaveBeenCalled();
  });

  it("shows a pending ask instead of offering it again", async () => {
    listCompanyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [marketRequest("a", "PENDING")],
    });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/Marktstatus angefragt/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Marktstatus anfragen" })).toBeNull();
  });

  it("offers the conversation once the status is visible and approachable", async () => {
    listCompanyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [marketRequest("a", "GRANTED")],
    });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Interesse zeigen" }));

    await waitFor(() => expect(expressInterest).toHaveBeenCalled());
  });

  it("withholds the conversation from someone who is not approachable", async () => {
    // Die Freigabe erlaubt zu sehen, nicht zu stören.
    listCompanyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [marketRequest("a", "GRANTED")],
    });
    getMarketStatus.mockResolvedValue({
      ok: true,
      status: marketStatus({ availability: "unavailable", is_approachable: false }),
    });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/Gerade nicht ansprechbar/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Interesse zeigen" })).toBeNull();
  });

  it("does not invent a reason when the status is gone", async () => {
    listCompanyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [marketRequest("a", "GRANTED")],
    });
    getMarketStatus.mockResolvedValue({ ok: true, status: null });
    renderWithProviders(<CandidatesRoute principal={principal(TENANT)} />);

    const note = await screen.findByText(/Marktstatus gerade nicht einsehbar/);
    expect(note.textContent).not.toMatch(/zurückgezogen|gelöscht|existiert/i);
  });
});
