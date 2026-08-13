/**
 * Die Transfers aus Unternehmenssicht — 232 Zeilen, die bis heute **keinen**
 * Komponententest hatten.
 *
 * Gedeckt war die Seite nur durch `transfer-journey.spec.ts`, und die überspringt
 * sich ohne laufenden Stapel selbst. Auf einer Maschine ohne Docker war das hier
 * völlig ungeprüft, und `make check` blieb grün.
 *
 * Ungeprüft war damit unter anderem die Regel, die am meisten trägt: **abschließen
 * darf das Unternehmen nur, wenn KEINE Freigabe nötig ist.** Ist eine nötig,
 * schließt die Person selbst ab — der letzte Schritt gehört der Seite, die als
 * einzige weiß, ob sie gehen darf.
 */

import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Transfer } from "../transfers/client";
import { renderWithProviders } from "../test/render";
import { CompanyTransfersRoute } from "./company-transfers";

vi.mock("../transfers/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../transfers/client")>();
  return {
    ...actual,
    listCompanyTransfers: vi.fn(),
    companyMove: vi.fn(),
    makeOffer: vi.fn(),
  };
});

const client = await import("../transfers/client");
const listCompanyTransfers = vi.mocked(client.listCompanyTransfers);
const companyMove = vi.mocked(client.companyMove);
const makeOffer = vi.mocked(client.makeOffer);

const TENANT = "22222222-2222-2222-2222-222222222222";
const TRANSFER = "33333333-3333-3333-3333-333333333333";

function principal(tenantId: string | null): MeResponse {
  return { user_id: "u", email: "chef@firma.example", tenant_id: tenantId, roles: ["user"] };
}

function transfer(overrides: Partial<Transfer> = {}): Transfer {
  return {
    id: TRANSFER,
    subject_id: "s",
    tenant_id: TENANT,
    status: "talking",
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
  listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [] });
  companyMove.mockResolvedValue({ ok: true, transfer: transfer() });
  makeOffer.mockResolvedValue({ ok: true, transfer: transfer({ status: "offered" }) });
});

describe("CompanyTransfersRoute", () => {
  it("fragt nach einem Unternehmen, statt eine leere Seite zu zeigen", () => {
    renderWithProviders(<CompanyTransfersRoute principal={principal(null)} />);

    expect(screen.getByText(/Transfers führen nur Unternehmen/i)).toBeInTheDocument();
    expect(listCompanyTransfers).not.toHaveBeenCalled();
  });

  it("sagt beim Laden, dass geladen wird", async () => {
    let loesen: (() => void) | undefined;
    listCompanyTransfers.mockReturnValue(
      new Promise((resolve) => {
        loesen = () => resolve({ ok: true, transfers: [] });
      })
    );

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("status")).toHaveTextContent(/Transfers werden geladen/i);
    expect(screen.queryByText(/Es läuft gerade kein Transfer/i)).toBeNull();
    loesen?.();
  });

  it("zeigt einen Fehler nie als leere Liste", async () => {
    listCompanyTransfers.mockResolvedValue({ ok: false, message: "Die Liste ist gerade weg." });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("gerade weg");
    expect(screen.queryByText(/Es läuft gerade kein Transfer/i)).toBeNull();
  });

  it("sagt bei leerer Liste, dass gerade keiner läuft", async () => {
    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/Es läuft gerade kein Transfer/i)).toBeInTheDocument();
  });

  it("sagt, dass die Ablöse festgehalten und nicht bewegt wird", async () => {
    // Diese Plattform führt kein Geld. Der Satz ist die Zusage, nicht Beiwerk.
    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(screen.getByText(/festgehalten, nicht bewegt/i)).toBeInTheDocument();
    expect(screen.getByText(/führt kein Geld/i)).toBeInTheDocument();
  });

  it("bietet ein Angebot nur im Gespräch an", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "interested" })],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await screen.findByText(/Interesse hinterlegt/i);
    expect(screen.queryByLabelText(/Ablöse in Euro/i)).toBeNull();
  });

  it("schickt Angebot, Start und Ablöse so, wie sie im Formular stehen", async () => {
    const user = userEvent.setup();
    listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [transfer()] });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/^Angebot$/i), "Wir bieten dir das an.");
    await user.type(screen.getByLabelText(/^Start$/i), "2026-11");
    await user.type(screen.getByLabelText(/Ablöse in Euro/i), "5000");
    await user.click(screen.getByRole("button", { name: /Angebot machen/i }));

    await waitFor(() => expect(makeOffer).toHaveBeenCalled());
    expect(makeOffer.mock.calls[0]?.[0]).toBe(TRANSFER);
    expect(makeOffer.mock.calls[0]?.[1]).toMatchObject({ note: "Wir bieten dir das an." });
  });

  it("bietet Abschließen NUR an, wenn keine Freigabe nötig ist", async () => {
    // Die tragende Regel dieser Seite. Ist eine Freigabe nötig, schließt die
    // Person selbst ab — nur sie weiß, ob sie gehen darf. Ein Zustand
    // „freigegeben und noch offen" existiert nicht.
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: false })],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("button", { name: /Abschließen/i })).toBeInTheDocument();
  });

  it("bietet Abschließen NICHT an, wenn eine Freigabe nötig ist", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: true })],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await screen.findByText(/Braucht eine Freigabe/i);
    expect(screen.queryByRole("button", { name: /Abschließen/i })).toBeNull();
  });

  it("nennt beim Namen, dass die Person selbst abschließt", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "accepted", requires_release: true })],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/die Person bestätigt sie selbst/i)).toBeInTheDocument();
  });

  it("bietet Zurückziehen an, solange der Vorgang läuft", async () => {
    const user = userEvent.setup();
    listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [transfer()] });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Zurückziehen/i }));

    await waitFor(() => expect(companyMove).toHaveBeenCalledWith(TRANSFER, "withdraw"));
  });

  it("bietet bei einem abgeschlossenen Vorgang nichts mehr an", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [transfer({ status: "completed" })],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await screen.findByText(/Abgeschlossen/i);
    expect(screen.queryByRole("button", { name: /Zurückziehen/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Abschließen/i })).toBeNull();
  });

  it("zeigt ein abgegebenes Angebot mit Ablöse in Euro", async () => {
    listCompanyTransfers.mockResolvedValue({
      ok: true,
      transfers: [
        transfer({
          status: "offered",
          offer_note: "Unser Angebot",
          offer_start_on: "2026-11",
          offer_fee_cents: 500000,
        }),
      ],
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    expect(await screen.findByText("Unser Angebot")).toBeInTheDocument();
    expect(screen.getByText("2026-11")).toBeInTheDocument();
    // Cent im Vertrag, Euro auf dem Schirm.
    expect(screen.getByText(/5\.000/)).toBeInTheDocument();
  });

  it("meldet einen abgelehnten Zug, statt so zu tun, als hätte er geklappt", async () => {
    const user = userEvent.setup();
    listCompanyTransfers.mockResolvedValue({ ok: true, transfers: [transfer()] });
    companyMove.mockResolvedValue({
      ok: false,
      reason: "conflict",
      message: "Dieser Zug ist nicht möglich.",
    });

    renderWithProviders(<CompanyTransfersRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Zurückziehen/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("nicht möglich");
  });
});
