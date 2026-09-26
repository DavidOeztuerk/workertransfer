import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { InvitationPage } from "./InvitationPage";

function answer(status: number, detail: string) {
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async () =>
        new Response(JSON.stringify({ title: "Request failed", detail }), {
          status,
          headers: { "content-type": "application/json" },
        }),
    ),
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

/*
 * A GERMAN PERSON READS GERMAN when the invitation is refused.
 *
 * identity-service answers 400 for two cases the person must answer
 * differently — an expired invitation (ask for a new one) and one issued for
 * another address (sign in with that one). Until 26.09.2026 the page drew the
 * English sentence raw.
 */
describe("InvitationPage", () => {
  it("says in German that the invitation has expired", async () => {
    answer(400, "This invitation has expired");
    renderWithStore(<InvitationPage />, { route: "/invitation?token=abc" });

    expect(
      await screen.findByText("Diese Einladung ist abgelaufen. Bitte lass dir eine neue schicken."),
    ).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/has expired/);
  });

  it("says in German that it was meant for another address", async () => {
    answer(400, "This invitation was issued for a different email address");
    renderWithStore(<InvitationPage />, { route: "/invitation?token=abc" });

    expect(
      await screen.findByText(/Bitte melde dich mit der eingeladenen Adresse an/),
    ).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/different email address/);
  });
});
