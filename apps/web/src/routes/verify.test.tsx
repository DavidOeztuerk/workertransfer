import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithProviders } from "../test/render";
import { VerifyRoute } from "./verify";

vi.mock("../auth/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/client")>();
  return { ...actual, verifyEmail: vi.fn(), resendVerification: vi.fn() };
});

const authClient = await import("../auth/client");
const verifyEmail = vi.mocked(authClient.verifyEmail);

beforeEach(() => {
  vi.clearAllMocks();
  verifyEmail.mockResolvedValue({ ok: true });
  // Je Test ein eigener Token — so, wie es in der Wirklichkeit ist. Ein
  // gemeinsamer würde von der Entdopplung über Testgrenzen hinweg getragen
  // und der zweite Test bekäme die Antwort des ersten. Dass genau das hier
  // auffiel, ist der Beleg, dass die Entdopplung wirkt.
  window.history.replaceState({}, "", `/verify?token=token-${crypto.randomUUID()}`);
});

describe("VerifyRoute", () => {
  it("burns a single-use token only once, however often the page mounts", async () => {
    // Der Fehler dahinter ist echt und war messbar: 2 von 120 POSTs auf
    // /auth/verify-email kamen im E2E-Lauf als HTTP 400 „ungültig" zurück. Der
    // Token ist einmalig — ein zweiter Aufruf verbraucht ihn nicht, er
    // scheitert. Und welche Antwort zuletzt ankam, entschied, was die Seite
    // anzeigte: die Person sah „Bestätigung fehlgeschlagen", obwohl ihr Konto
    // gerade freigeschaltet worden war.
    //
    // Ein zweiter Aufbau genügt dafür, und den gibt es reichlich: HMR im
    // Dev-Server, StrictMode, ein Reload, ein Router, der neu einhängt.
    const first = renderWithProviders(<VerifyRoute />);
    expect(await screen.findByRole("heading", { name: "E-Mail bestätigt" })).toBeInTheDocument();

    first.unmount();
    renderWithProviders(<VerifyRoute />);

    // Derselbe Ausgang wie beim ersten Mal — und kein zweiter Aufruf.
    expect(await screen.findByRole("heading", { name: "E-Mail bestätigt" })).toBeInTheDocument();
    expect(verifyEmail).toHaveBeenCalledTimes(1);
  });

  it("says the link is missing rather than asking the server about nothing", async () => {
    window.history.replaceState({}, "", "/verify");

    renderWithProviders(<VerifyRoute />);

    expect(await screen.findByText(/fehlt ein Bestätigungslink/i)).toBeInTheDocument();
    expect(verifyEmail).not.toHaveBeenCalled();
  });

  it("keeps the three headings apart — loading is not success", async () => {
    // Die Seite hat drei Zustände, und zwei davon sagen „bestätigt". Ein Test,
    // der auf einen Teilstring prüft, ist schon bei der Ladeanzeige zufrieden.
    verifyEmail.mockResolvedValue({ ok: false, expired: true, message: "Der Link ist abgelaufen." });

    renderWithProviders(<VerifyRoute />);

    expect(
      await screen.findByRole("heading", { name: "Bestätigung fehlgeschlagen" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "E-Mail bestätigt" })).toBeNull();
  });
});
