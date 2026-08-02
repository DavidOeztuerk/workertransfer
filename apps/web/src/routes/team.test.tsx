import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { CompanyMember, Invitation } from "../auth/team";
import { renderWithProviders } from "../test/render";
import { TeamRoute } from "./team";

vi.mock("../auth/team", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/team")>();
  return {
    ...actual,
    listMembers: vi.fn(),
    listInvitations: vi.fn(),
    inviteMember: vi.fn(),
    withdrawInvitation: vi.fn(),
  };
});

const client = await import("../auth/team");
const listMembers = vi.mocked(client.listMembers);
const listInvitations = vi.mocked(client.listInvitations);
const inviteMember = vi.mocked(client.inviteMember);
const withdrawInvitation = vi.mocked(client.withdrawInvitation);

const TENANT = "11111111-1111-1111-1111-111111111111";
const ME = "33333333-3333-3333-3333-333333333333";

function principal(tenantId: string | null): MeResponse {
  return { user_id: ME, email: "chef@firma.example", tenant_id: tenantId, roles: ["user"] };
}

function member(role: "admin" | "member", id = ME, name = "Chefin"): CompanyMember {
  return { user_id: id, display_name: name, role };
}

function invitation(overrides: Partial<Invitation> = {}): Invitation {
  return {
    id: "22222222-2222-2222-2222-222222222222",
    email: "neu@firma.example",
    role: "member",
    status: "pending",
    created_at: "2026-08-02T10:00:00Z",
    expires_at: "2026-08-09T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  listMembers.mockResolvedValue({ ok: true, members: [member("admin")] });
  listInvitations.mockResolvedValue({ ok: true, invitations: [] });
  inviteMember.mockResolvedValue({ ok: true, invitation: invitation() });
  withdrawInvitation.mockResolvedValue({ ok: true });
});

describe("TeamRoute", () => {
  it("lists the people who may act for the company", async () => {
    listMembers.mockResolvedValue({
      ok: true,
      members: [member("admin"), member("member", "other", "Kollege")],
    });

    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    expect(await screen.findByText("Chefin")).toBeInTheDocument();
    expect(screen.getByText("Kollege")).toBeInTheDocument();
  });

  it("offers the invite form to an admin", async () => {
    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    expect(await screen.findByLabelText(/E-Mail/i)).toBeInTheDocument();
  });

  it("does not offer it to a plain member", async () => {
    // Sichtbarkeit ist Bequemlichkeit; die Ablehnung spricht immer der Server
    // aus (403). Aber ein Formular anzubieten, das sicher scheitert, ist eine
    // Einladung zur Enttäuschung.
    listMembers.mockResolvedValue({ ok: true, members: [member("member")] });

    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    await screen.findByText("Chefin");
    expect(screen.queryByLabelText(/E-Mail/i)).toBeNull();
  });

  it("invites with the address and the chosen role", async () => {
    const user = userEvent.setup();
    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/E-Mail/i), "neu@firma.example");
    await user.selectOptions(screen.getByLabelText(/Rolle/i), "admin");
    await user.click(screen.getByRole("button", { name: /Einladen/i }));

    await waitFor(() =>
      expect(inviteMember).toHaveBeenCalledWith(TENANT, "neu@firma.example", "admin")
    );
  });

  it("says the same thing whether or not the address already has an account", async () => {
    const user = userEvent.setup();
    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/E-Mail/i), "neu@firma.example");
    await user.click(screen.getByRole("button", { name: /Einladen/i }));

    // Der Server antwortet in beiden Fällen gleich; die Oberfläche darf daraus
    // keinen Unterschied machen ("Konto gefunden" wäre genau das Leck).
    const note = await screen.findByText(/Einladung verschickt/i);
    expect(note.textContent).not.toMatch(/Konto|registriert|bereits/i);
  });

  it("shows an open invitation without ever showing a token", async () => {
    listInvitations.mockResolvedValue({ ok: true, invitations: [invitation()] });

    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    const item = within(await screen.findByTestId("invitation-list"));
    expect(item.getByText(/neu@firma.example/)).toBeInTheDocument();
    expect(item.queryByText(/token/i)).toBeNull();
  });

  it("withdraws an invitation through the server", async () => {
    const user = userEvent.setup();
    listInvitations.mockResolvedValue({ ok: true, invitations: [invitation()] });
    renderWithProviders(<TeamRoute principal={principal(TENANT)} />);

    await user.click(await screen.findByRole("button", { name: /Zurückziehen/i }));

    await waitFor(() =>
      expect(withdrawInvitation).toHaveBeenCalledWith(TENANT, invitation().id)
    );
  });

  it("asks for a company instead of showing an empty page", async () => {
    renderWithProviders(<TeamRoute principal={principal(null)} />);

    expect(await screen.findByText(/Unternehmen/i)).toBeInTheDocument();
    expect(listMembers).not.toHaveBeenCalled();
  });
});
