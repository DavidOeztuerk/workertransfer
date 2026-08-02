import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithProviders } from "../test/render";
import { InvitationRoute } from "./invitation";

vi.mock("../auth/team", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/team")>();
  return { ...actual, acceptInvitation: vi.fn() };
});

const client = await import("../auth/team");
const acceptInvitation = vi.mocked(client.acceptInvitation);

function withToken(token: string | null) {
  const search = token === null ? "" : `?token=${token}`;
  window.history.replaceState({}, "", `/invitation${search}`);
}

beforeEach(() => {
  vi.clearAllMocks();
  acceptInvitation.mockResolvedValue({
    ok: true,
    membership: { id: "t", name: "Firma", domain: "firma.example", role: "member" },
  });
});

describe("InvitationRoute", () => {
  it("accepts the token from the link", async () => {
    withToken("abc123");

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByText(/Firma/)).toBeInTheDocument();
    expect(acceptInvitation).toHaveBeenCalledWith("abc123");
  });

  it("does not call the server without a token", async () => {
    withToken(null);

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(acceptInvitation).not.toHaveBeenCalled();
  });

  it("sends an anonymous visitor to the login instead of blaming the link", async () => {
    // Der häufigste Fall: die Mail wird geöffnet, bevor es ein Konto gibt.
    withToken("abc123");
    acceptInvitation.mockResolvedValue({
      ok: false,
      reason: "unauthenticated",
      message: "Bitte melde dich mit der eingeladenen Adresse an.",
    });

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByRole("link", { name: /Anmelden/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Registrieren/i })).toBeInTheDocument();
  });

  it("passes a refusal through in the server's words", async () => {
    withToken("abc123");
    acceptInvitation.mockResolvedValue({
      ok: false,
      reason: "refused",
      message: "This invitation has expired",
    });

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByRole("alert")).toHaveTextContent("expired");
  });

  it("tells you to act for the company on purpose, rather than doing it silently", async () => {
    // Ein automatischer Wechsel würde jemanden ungefragt aus dem Unternehmen
    // herausbefördern, in dem er gerade arbeitet (ADR-0018).
    withToken("abc123");

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByText(/oben.*wechseln|Unternehmen wechseln/i)).toBeInTheDocument();
  });
});
