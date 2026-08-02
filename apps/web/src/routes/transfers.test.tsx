import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Transfer } from "../transfers/client";
import { renderWithProviders } from "../test/render";
import { CompanyTransfersRoute } from "./company-transfers";
import { TransfersRoute } from "./transfers";

vi.mock("../transfers/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../transfers/client")>();
  return {
    ...actual,
    listMyTransfers: vi.fn(),
    listCompanyTransfers: vi.fn(),
    personMove: vi.fn(),
    companyMove: vi.fn(),
    makeOffer: vi.fn(),
  };
});

const client = await import("../transfers/client");
const listMyTransfers = vi.mocked(client.listMyTransfers);
const listCompanyTransfers = vi.mocked(client.listCompanyTransfers);
const personMove = vi.mocked(client.personMove);
const companyMove = vi.mocked(client.companyMove);
const makeOffer = vi.mocked(client.makeOffer);

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TENANT = "33333333-3333-3333-3333-333333333333";
const TRANSFER = "44444444-4444-4444-4444-444444444444";

function person(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function company(): MeResponse {
  return { user_id: "55555555-5555-5555-5555-555555555555", email: "hr@acme.de", tenant_id: TENANT, roles: ["user"] };
}

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: TRANSFER,
    subject_id: SUBJECT,
    tenant_id: TENANT,
    status: "interested",
    requires_release: false,
    release_confirmed: false,
    message: "Wir würden gern mit dir sprechen.",
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
  listMyTransfers.mockResolvedValue({ ok: true, transfers: [] });
  listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [] });
  personMove.mockResolvedValue({ ok: true, transfer: transfer({ status: "talking" }) });
  companyMove.mockResolvedValue({ ok: true, transfer: transfer({ status: "completed" }) });
  makeOffer.mockResolvedValue({ ok: true, transfer: transfer({ status: "offered" }) });
});

describe("TransfersRoute", () => {
  it("offers declining in every running state — from a trap there is no exit", async () => {
    const states: Transfer["status"][] = ["interested", "talking", "offered", "accepted"];
    for (const status of states) {
      listMyTransfers.mockResolvedValue({ ok: true, transfers: [transfer({ status })] });
      const view = renderWithProviders(<TransfersRoute principal={person()} />);
      expect(await screen.findByRole("button", { name: "Ablehnen" })).toBeTruthy();
      view.unmount();
    }
  });

  it("does not offer declining once the matter is settled", async () => {
    listMyTransfers.mockResolvedValue({ ok: true, transfers: [transfer({ status: "completed" })] });
    renderWithProviders(<TransfersRoute principal={person()} />);

    expect(await screen.findByText("Abgeschlossen")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Ablehnen" })).toBeNull();
  });

  it("accepts the conversation", async () => {
    listMyTransfers.mockResolvedValue({ ok: true, transfers: [transfer()] });
    renderWithProviders(<TransfersRoute principal={person()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Gespräch annehmen" }));

    await waitFor(() => expect(personMove).toHaveBeenCalledWith(TRANSFER, "accept-talk"));
  });

  it("asks for the release only when one is needed and still missing", async () => {
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: true })],
    });
    renderWithProviders(<TransfersRoute principal={person()} />);
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Freigabe bestätigen und abschließen" })
    );

    await waitFor(() => expect(personMove).toHaveBeenCalledWith(TRANSFER, "confirm-release"));
  });

  it("says that the platform will not contact the current employer", async () => {
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: true })],
    });
    renderWithProviders(<TransfersRoute principal={person()} />);

    expect(await screen.findByText(/Diese Plattform fragt ihn nicht/)).toBeTruthy();
  });

  it("shows the offer in euros, not in cents", async () => {
    listMyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "offered", offer_fee_cents: 500000 })],
    });
    renderWithProviders(<TransfersRoute principal={person()} />);

    expect(await screen.findByText(/5.000,00/)).toBeTruthy();
  });
});

describe("CompanyTransfersRoute", () => {
  it("sends people without an active company to switch first", async () => {
    renderWithProviders(<CompanyTransfersRoute principal={person()} />);

    expect(await screen.findByText(/Transfers führen nur Unternehmen/)).toBeTruthy();
    expect(listCompanyTransfers).not.toHaveBeenCalled();
  });

  it("turns euros into cents when making an offer", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "talking" })],
    });
    renderWithProviders(<CompanyTransfersRoute principal={company()} />);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/^Angebot/), "Teamleitung");
    await user.type(screen.getByLabelText(/^Start/), "2026-11");
    await user.type(screen.getByLabelText(/Ablöse in Euro/), "5000");
    await user.click(screen.getByRole("button", { name: "Angebot machen" }));

    await waitFor(() =>
      expect(makeOffer).toHaveBeenCalledWith(TRANSFER, {
        note: "Teamleitung",
        start_on: "2026-11",
        fee_cents: 500000,
      })
    );
  });

  it("sends null rather than an empty string for an omitted start", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "talking" })],
    });
    renderWithProviders(<CompanyTransfersRoute principal={company()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Angebot machen" }));

    await waitFor(() =>
      expect(makeOffer).toHaveBeenCalledWith(TRANSFER, {
        note: "",
        start_on: null,
        fee_cents: null,
      })
    );
  });

  it("never offers completion when a release is required", async () => {
    // Dann schließt die Bestätigung der Person selbst ab. Ein Knopf hier wäre
    // ein zweiter Weg an dasselbe Ergebnis — über die Seite, die als einzige
    // nicht weiß, ob die Person gehen darf.
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: true })],
    });
    renderWithProviders(<CompanyTransfersRoute principal={company()} />);

    expect(await screen.findByText(/Braucht eine Freigabe/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Abschließen" })).toBeNull();
  });

  it("completes when no release is needed — then it is the employer's call", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: false })],
    });
    renderWithProviders(<CompanyTransfersRoute principal={company()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Abschließen" }));

    await waitFor(() => expect(companyMove).toHaveBeenCalledWith(TRANSFER, "complete"));
  });

  it("reports a refused step instead of pretending it worked", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted" })],
    });
    companyMove.mockResolvedValue({
      ok: false,
      reason: "conflict",
      message: "Dieser Schritt passt nicht zum aktuellen Stand.",
    });
    renderWithProviders(<CompanyTransfersRoute principal={company()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Abschließen" }));

    expect(await screen.findByRole("alert")).toBeTruthy();
  });
});
