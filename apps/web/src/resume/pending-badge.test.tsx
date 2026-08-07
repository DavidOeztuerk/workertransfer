import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ResumeRequest } from "./client";
import { renderWithProviders } from "../test/render";
import { PendingRequestBadge } from "./pending-badge";

vi.mock("./client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./client")>();
  return { ...actual, listMyRequests: vi.fn() };
});

const client = await import("./client");
const listMyRequests = vi.mocked(client.listMyRequests);

function request(status: ResumeRequest["status"]): ResumeRequest {
  return {
    id: crypto.randomUUID(),
    subject_id: "s",
    tenant_id: "t",
    status,
    created_at: "2026-08-02T10:00:00Z",
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listMyRequests.mockResolvedValue({ ok: true, requests: [] });
});

describe("PendingRequestBadge", () => {
  it("counts only what still needs an answer", async () => {
    listMyRequests.mockResolvedValue({
      ok: true,
      requests: [request("PENDING"), request("PENDING"), request("GRANTED"), request("DECLINED")],
    });

    renderWithProviders(<PendingRequestBadge />);

    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("shows nothing when there is nothing to answer", async () => {
    listMyRequests.mockResolvedValue({ ok: true, requests: [request("GRANTED")] });

    const { container } = renderWithProviders(<PendingRequestBadge />);

    await vi.waitFor(() => expect(listMyRequests).toHaveBeenCalled());
    expect(container.textContent).toBe("");
  });

  it("stays silent when the list cannot be loaded", async () => {
    // Eine Zahl, die auf einem Fehler beruht, wäre schlimmer als keine Zahl:
    // sie würde die Person entweder unnötig beunruhigen oder in Sicherheit
    // wiegen.
    listMyRequests.mockResolvedValue({ ok: false, message: "Ledger antwortet nicht." });

    const { container } = renderWithProviders(<PendingRequestBadge />);

    await vi.waitFor(() => expect(listMyRequests).toHaveBeenCalled());
    expect(container.textContent).toBe("");
  });

  it("says in words what the number means", async () => {
    listMyRequests.mockResolvedValue({ ok: true, requests: [request("PENDING")] });

    renderWithProviders(<PendingRequestBadge />);

    // Eine nackte Ziffer neben einem Link ist für einen Screenreader nichts.
    expect(await screen.findByLabelText(/1 offene Anfrage/i)).toBeInTheDocument();
  });
});
