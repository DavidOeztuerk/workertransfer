import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Resume, ResumeRequest } from "../resume/client";
import { renderWithProviders } from "../test/render";
import { ResumeRoute } from "./resume";

vi.mock("../resume/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../resume/client")>();
  return {
    ...actual,
    getMyResume: vi.fn(),
    saveMyResume: vi.fn(),
    listMyRequests: vi.fn(),
    answerRequest: vi.fn(),
    revokeAccess: vi.fn(),
  };
});

const client = await import("../resume/client");
const getMyResume = vi.mocked(client.getMyResume);
const saveMyResume = vi.mocked(client.saveMyResume);
const listMyRequests = vi.mocked(client.listMyRequests);
const answerRequest = vi.mocked(client.answerRequest);
const revokeAccess = vi.mocked(client.revokeAccess);

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const REQUEST_ID = "22222222-2222-2222-2222-222222222222";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function resume(): Resume {
  return {
    subject_id: SUBJECT,
    positions: [
      {
        employer: "Acme GmbH",
        title: "Backend-Entwicklerin",
        started_on: "2020-01",
        ended_on: null,
        description: "",
      },
    ],
    education: [],
    updated_at: "2026-08-02T10:00:00Z",
  };
}

function request(overrides: Partial<ResumeRequest> = {}): ResumeRequest {
  return {
    id: REQUEST_ID,
    subject_id: SUBJECT,
    tenant_id: "33333333-3333-3333-3333-333333333333",
    status: "PENDING",
    created_at: "2026-08-02T10:00:00Z",
    active: false,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyResume.mockResolvedValue(null);
  listMyRequests.mockResolvedValue({ ok: true, requests: [] });
  saveMyResume.mockResolvedValue({ ok: true, resume: resume() });
  answerRequest.mockResolvedValue({ ok: true, request: request({ status: "GRANTED" }) });
  revokeAccess.mockResolvedValue({ ok: true, request: request({ status: "GRANTED" }) });
});

describe("ResumeRoute", () => {
  it("fills the form with what is already stored", async () => {
    getMyResume.mockResolvedValue(resume());

    renderWithProviders(<ResumeRoute principal={principal()} />);

    expect(await screen.findByDisplayValue("Acme GmbH")).toBeInTheDocument();
  });

  it("treats an empty end date as 'still there' rather than as a gap", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ResumeRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Station hinzufügen/i }));
    await user.type(screen.getByLabelText(/Arbeitgeber/i), "Acme GmbH");
    await user.type(screen.getByLabelText(/Position/i), "Entwicklerin");
    await user.type(screen.getByLabelText(/Von/i), "2020-01");
    await user.click(screen.getByRole("button", { name: /Speichern/i }));

    await waitFor(() => expect(saveMyResume).toHaveBeenCalled());
    expect(saveMyResume.mock.calls[0]?.[0].positions[0]?.ended_on).toBeNull();
  });

  it("keeps a rejected form on screen and does not claim success", async () => {
    const user = userEvent.setup();
    getMyResume.mockResolvedValue(resume());
    saveMyResume.mockResolvedValue({
      ok: false,
      reason: "invalid",
      message: "Only one position may be left open",
    });
    renderWithProviders(<ResumeRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Speichern/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("one position");
    expect(screen.queryByText(/gespeichert/i)).toBeNull();
  });

  it("offers a pending request both answers", async () => {
    listMyRequests.mockResolvedValue({ ok: true, requests: [request()] });

    renderWithProviders(<ResumeRoute principal={principal()} />);

    const item = within(await screen.findByRole("listitem"));
    expect(item.getByRole("button", { name: /Freigeben/i })).toBeInTheDocument();
    expect(item.getByRole("button", { name: /Ablehnen/i })).toBeInTheDocument();
  });

  it("passes a decline through as a decline, not as a grant", async () => {
    const user = userEvent.setup();
    listMyRequests.mockResolvedValue({ ok: true, requests: [request()] });
    renderWithProviders(<ResumeRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Ablehnen/i }));

    await waitFor(() => expect(answerRequest).toHaveBeenCalledWith(REQUEST_ID, false));
  });

  it("offers a withdrawal only while access actually holds", async () => {
    // status GRANTED, active false: schon widerrufen. Ein zweiter
    // Widerruf-Knopf wäre eine Lüge über den Zustand.
    listMyRequests.mockResolvedValue({
      ok: true,
      requests: [request({ status: "GRANTED", active: false })],
    });

    renderWithProviders(<ResumeRoute principal={principal()} />);

    await screen.findByRole("listitem");
    expect(screen.queryByRole("button", { name: /Zurückziehen/i })).toBeNull();
  });

  it("withdraws through the server, never by building a capability", async () => {
    const user = userEvent.setup();
    listMyRequests.mockResolvedValue({
      ok: true,
      requests: [request({ status: "GRANTED", active: true })],
    });
    renderWithProviders(<ResumeRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Zurückziehen/i }));

    await waitFor(() => expect(revokeAccess).toHaveBeenCalledWith(REQUEST_ID));
  });

  it("says a declined request stays declined instead of offering it again", async () => {
    listMyRequests.mockResolvedValue({ ok: true, requests: [request({ status: "DECLINED" })] });

    renderWithProviders(<ResumeRoute principal={principal()} />);

    await screen.findByRole("listitem");
    expect(screen.queryByRole("button", { name: /Freigeben/i })).toBeNull();
  });

  it("shows nothing rather than an empty list when the ledger is silent", async () => {
    listMyRequests.mockResolvedValue({ ok: false, message: "Der Consent-Ledger antwortet nicht." });

    renderWithProviders(<ResumeRoute principal={principal()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Consent-Ledger");
    expect(screen.queryByRole("listitem")).toBeNull();
  });

  it("tells an anonymous visitor to log in", () => {
    renderWithProviders(<ResumeRoute principal={null} />);

    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });
});
