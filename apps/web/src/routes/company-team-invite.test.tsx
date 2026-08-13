import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { CompanyMember, Invitation } from "../auth/team";
import { renderWithProviders } from "../test/render";
import { CompanyTeamInviteRoute } from "./company-team-invite";

vi.mock("../auth/team", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/team")>();
  return {
    ...actual,
    listMembers: vi.fn(),
    listInvitations: vi.fn(),
    inviteMember: vi.fn(),
    withdrawInvitation: vi.fn(),
    removeMember: vi.fn(),
  };
});

const client = await import("../auth/team");
const listMembers = vi.mocked(client.listMembers);
const listInvitations = vi.mocked(client.listInvitations);
const inviteMember = vi.mocked(client.inviteMember);
const withdrawInvitation = vi.mocked(client.withdrawInvitation);
const removeMember = vi.mocked(client.removeMember);

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
  removeMember.mockResolvedValue({ ok: true });
});

describe("CompanyTeamInviteRoute", () => {
  it("offers the invite form to an admin", async () => {
    renderWithProviders(<CompanyTeamInviteRoute principal={principal(TENANT)} />);

    expect(await screen.findByLabelText(/E-Mail/i)).toBeInTheDocument();
  });

  it("invites with the address and the chosen role", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyTeamInviteRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/E-Mail/i), "neu@firma.example");
    await user.selectOptions(screen.getByLabelText(/Rolle/i), "admin");
    await user.click(screen.getByRole("button", { name: /Einladen/i }));

    await waitFor(() =>
      expect(inviteMember).toHaveBeenCalledWith(TENANT, "neu@firma.example", "admin")
    );
  });

  it("says the same thing whether or not the address already has an account", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CompanyTeamInviteRoute principal={principal(TENANT)} />);

    await user.type(await screen.findByLabelText(/E-Mail/i), "neu@firma.example");
    await user.click(screen.getByRole("button", { name: /Einladen/i }));

    // Der Server antwortet in beiden Fällen gleich; die Oberfläche darf daraus
    // keinen Unterschied machen ("Konto gefunden" wäre genau das Leck).
    const note = await screen.findByText(/Einladung verschickt/i);
    expect(note.textContent).not.toMatch(/Konto|registriert|bereits/i);
  });

  it("bietet einem einfachen Mitglied kein Formular an", async () => {
    // Die Rollenprüfung ist mitgewandert. Ein abgespaltenes Formular, das sie
    // zurückließe, wäre eine Tür neben der verschlossenen.
    listMembers.mockResolvedValue({
      ok: true,
      members: [{ user_id: "u", display_name: "Ich", role: "member" }],
    });

    renderWithProviders(<CompanyTeamInviteRoute principal={principal(TENANT)} />);

    expect(await screen.findByText(/nur Administratoren/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/E-Mail-Adresse/i)).toBeNull();
  });

  it("bietet den Rückweg an", async () => {
    renderWithProviders(<CompanyTeamInviteRoute principal={principal(TENANT)} />);

    expect(await screen.findByRole("link", { name: /Zurück zur Mannschaft/i })).toHaveAttribute(
      "href",
      "/company/team"
    );
  });
});
