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

/** Setzt den Link — und gibt den Token zurück, den er trägt.
 *
 * Standardmäßig ein FRISCHER Token je Test, wie auf der Bestätigungsseite. Die
 * Seite merkt sich je Token die Zusage modulweit (der Token ist einmalig), und
 * ein zwischen Tests geteilter Token trüge das Ergebnis des vorigen Tests in
 * den nächsten — der Mock des zweiten liefe dann ins Leere. Genau das ist beim
 * Einbau des Riegels passiert: zwei bestehende Tests wurden rot, obwohl die
 * Seite stimmte.
 */
function withToken(token: string | null = `token-${crypto.randomUUID()}`): string | null {
  const search = token === null ? "" : `?token=${token}`;
  window.history.replaceState({}, "", `/invitation${search}`);
  return token;
}

beforeEach(() => {
  vi.clearAllMocks();
  acceptInvitation.mockResolvedValue({
    ok: true,
    membership: { id: "t", name: "Firma", domain: "firma.example", role: "member" },
  });
});

describe("InvitationRoute", () => {
  it("uses a single-use token exactly once, even across a second mount", async () => {
    // Dieselbe Falle wie auf der Bestätigungsseite, und sie war hier noch
    // offen: der Einladungstoken ist EINMALIG. Ein zweiter Aufbau (HMR,
    // StrictMode, Reload, ein neu einhängender Router) schickt ihn erneut, der
    // zweite Aufruf scheitert — und weil beide Antworten in denselben Zustand
    // schreiben, entscheidet die zuletzt eintreffende. Im schlechten Fall
    // steht „Einladung nicht angenommen" auf dem Schirm, während die Person
    // dem Unternehmen gerade beigetreten ist.
    //
    // Gefunden als Wackler im E2E-Lauf: die Überschrift mit dem Firmennamen
    // blieb beim ersten Anlauf aus, beim zweiten war sie da.
    const token = withToken();
    expect(token).not.toBeNull();

    const first = renderWithProviders(<InvitationRoute />);
    expect(await screen.findByRole("heading", { name: /Willkommen bei Firma/ })).toBeInTheDocument();

    first.unmount();
    renderWithProviders(<InvitationRoute />);

    // Derselbe Ausgang wie beim ersten Mal — und kein zweiter Aufruf.
    expect(await screen.findByRole("heading", { name: /Willkommen bei Firma/ })).toBeInTheDocument();
    expect(acceptInvitation).toHaveBeenCalledTimes(1);
  });

  it("accepts the token from the link", async () => {
    const token = withToken();

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByText(/Firma/)).toBeInTheDocument();
    expect(acceptInvitation).toHaveBeenCalledWith(token);
  });

  it("does not call the server without a token", async () => {
    withToken(null);

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(acceptInvitation).not.toHaveBeenCalled();
  });

  it("sends an anonymous visitor to the login instead of blaming the link", async () => {
    // Der häufigste Fall: die Mail wird geöffnet, bevor es ein Konto gibt.
    withToken();
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
    withToken();
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
    withToken();

    renderWithProviders(<InvitationRoute />);

    expect(await screen.findByText(/oben.*wechseln|Unternehmen wechseln/i)).toBeInTheDocument();
  });
});
