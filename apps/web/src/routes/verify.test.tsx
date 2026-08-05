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
  window.history.replaceState({}, "", "/verify?token=ein-token");
});

describe("VerifyRoute", () => {
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
