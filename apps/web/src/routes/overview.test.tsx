import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Transfer } from "../transfers/client";
import { renderWithProviders } from "../test/render";
import { OverviewRoute } from "./overview";

vi.mock("../market/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../market/client")>();
  return { ...actual, listMyMarketRequests: vi.fn() };
});
vi.mock("../resume/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../resume/client")>();
  return { ...actual, listMyRequests: vi.fn() };
});
vi.mock("../transfers/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../transfers/client")>();
  return { ...actual, listMyTransfers: vi.fn(), listCompanyTransfers: vi.fn() };
});

const market = await import("../market/client");
const listMyMarketRequests = vi.mocked(market.listMyMarketRequests);
const resume = await import("../resume/client");
const listMyRequests = vi.mocked(resume.listMyRequests);
const transfers = await import("../transfers/client");
const listMyTransfers = vi.mocked(transfers.listMyTransfers);
const listCompanyTransfers = vi.mocked(transfers.listCompanyTransfers);

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TENANT = "22222222-2222-2222-2222-222222222222";

function person(tenantId: string | null = null): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: tenantId, roles: ["user"] };
}

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: crypto.randomUUID(),
    subject_id: SUBJECT,
    tenant_id: TENANT,
    status: "interested",
    requires_release: false,
    release_confirmed: false,
    message: "",
    offer_note: "",
    offer_start_on: null,
    offer_fee_cents: null,
    created_at: "2026-08-02T10:00:00Z",
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listMyMarketRequests.mockResolvedValue({ ok: true, requests: [] });
  listMyRequests.mockResolvedValue({ ok: true, requests: [] });
  listMyTransfers.mockResolvedValue({ ok: true, transfers: [] });
  listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [] });
});

describe("OverviewRoute", () => {
  it("says plainly when nothing is waiting", async () => {
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(await screen.findByText("Gerade wartet nichts auf dich.")).toBeTruthy();
  });

  it("counts only what waits for a decision, not what is simply running", async () => {
    // Eine Übersicht, die auch anzeigt, was von selbst läuft, ist eine Liste —
    // und Listen übersieht man.
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [
        transfer({ status: "interested" }), // wartet auf mich
        transfer({ status: "talking" }), // läuft, das Unternehmen ist dran
        transfer({ status: "completed" }), // vorbei
      ],
    });
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(await screen.findByText("1 Gespräch wartet auf dich")).toBeTruthy();
  });

  it("counts a pending release, but not one already confirmed", async () => {
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [
        transfer({ status: "accepted", requires_release: true }),
        transfer({ status: "accepted", requires_release: true, release_confirmed: true }),
      ],
    });
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(await screen.findByText("1 Gespräch wartet auf dich")).toBeTruthy();
  });

  it("counts open requests of both kinds separately", async () => {
    listMyMarketRequests.mockResolvedValue({
      ok: true,
      requests: [
        {
          id: "a",
          subject_id: SUBJECT,
          tenant_id: TENANT,
          status: "PENDING",
          created_at: "2026-08-02T10:00:00Z",
          answered_at: null,
          active: null,
        },
        {
          id: "b",
          subject_id: SUBJECT,
          tenant_id: TENANT,
          status: "GRANTED",
          created_at: "2026-08-02T10:00:00Z",
          answered_at: null,
          active: true,
        },
      ],
    });
    listMyRequests.mockResolvedValue({
      ok: true,
      requests: [
        {
          id: "c",
          subject_id: SUBJECT,
          tenant_id: TENANT,
          status: "PENDING",
          created_at: "2026-08-02T10:00:00Z",
          active: false,
        },
      ],
    });
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(
      await screen.findByText("1 Unternehmen möchte sehen, ob du ansprechbar bist")
    ).toBeTruthy();
    expect(screen.getByText("1 Anfrage nach deinem Lebenslauf")).toBeTruthy();
  });

  it("does not ask about company transfers without an active company", async () => {
    renderWithProviders(<OverviewRoute principal={person(null)} />);

    await screen.findByText("Gerade wartet nichts auf dich.");
    expect(listCompanyTransfers).not.toHaveBeenCalled();
  });

  it("keeps the company's business apart from the person's", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "talking" })],
    });
    renderWithProviders(<OverviewRoute principal={person(TENANT)} />);

    expect(await screen.findByText("1 Transfer wartet auf euch")).toBeTruthy();
    expect(screen.getByText("Für dein Unternehmen")).toBeTruthy();
    expect(screen.queryByText("Für dich")).toBeNull();
  });

  it("admits an incomplete picture instead of claiming nothing is waiting", async () => {
    // „Nichts liegt an" ist die eine Aussage, die nach einer fehlgeschlagenen
    // Abfrage falsch sein kann — und sie wiegt in Sicherheit.
    listMyMarketRequests.mockResolvedValue({ ok: false, message: "Ledger schweigt" });
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("Gerade wartet nichts auf dich.")).toBeNull();
  });

  it("uses singular and plural, because a counter that says '1 Gespräche' looks broken", async () => {
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "interested" }), transfer({ status: "offered" })],
    });
    renderWithProviders(<OverviewRoute principal={person()} />);

    expect(await screen.findByText("2 Gespräche warten auf dich")).toBeTruthy();
  });
});
