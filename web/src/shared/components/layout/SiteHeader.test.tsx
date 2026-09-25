import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import type { Session } from "../../../features/auth/types/session";
import type { OccupationalFieldChoice } from "../../lib/occupationalFields";
import { SiteHeader } from "./SiteHeader";

/**
 * The navigation follows the occupational field (ADR-0039). Three cases — the
 * middle one shows that GitHub is not devalued. It only hides; the route stays
 * reachable.
 */
describe("SiteHeader and the occupational field", () => {
  function account(occupationalField: OccupationalFieldChoice): Session {
    return {
      userId: "11111111-1111-1111-1111-111111111111",
      email: "anna@example.com",
      tenantId: null,
      language: "de",
      displayName: "Anna Beispiel",
      givenName: "",
      familyName: "",
      berufsfeld: occupationalField,
    };
  }

  async function openAccountMenu(occupationalField: OccupationalFieldChoice) {
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, {
      auth: { status: "authenticated", session: account(occupationalField) },
    });
    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
  }

  it("shows GitHub as long as nobody has named an occupational field", async () => {
    await openAccountMenu(null);

    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("still shows GitHub for it_software", async () => {
    await openAccountMenu("it_software");

    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("carries appearance and language as ONE row that shows what applies", async () => {
    // A row like any other, naming the value that applies.
    await openAccountMenu(null);

    const row = screen.getByRole("menuitem", { name: /Darstellung/ });
    expect(row).toHaveTextContent("System");
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("goes one level deeper instead of opening a second overlay", async () => {
    // The same surface swaps its content instead of opening a second overlay.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, {
      auth: { status: "authenticated", session: account(null) },
    });
    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
    await user.click(screen.getByRole("menuitem", { name: /Darstellung/ }));

    expect(screen.queryByRole("menuitem", { name: "Profil" })).not.toBeInTheDocument();
    expect(
      screen.getByRole("menuitemradio", { name: /System/ }),
    ).toBeChecked();

    // The choice closes the menu: the effect is visible at once instead of
    // lying behind a modal.
    await user.click(screen.getByRole("menuitemradio", { name: "Dunkel" }));

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
    expect(screen.getByRole("menuitem", { name: /Darstellung/ })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("puts \"Abmelden\" (sign out) at the end, below the preferences", async () => {
    await openAccountMenu(null);

    const entries = screen.getAllByRole("menuitem").map((e) => e.textContent ?? "");
    expect(entries[entries.length - 1]).toContain("Abmelden");
  });

  it("names in the account menu WHO is signed in", async () => {
    // On a platform that decides about releases, "under which account am I
    // doing this?" is no trifle — the avatar shows only two letters.
    await openAccountMenu(null);

    expect(screen.getByText("anna@example.com")).toBeInTheDocument();
  });

  it("does NOT call the signed-out button \"Mein Konto\"", async () => {
    // The name is also the signal by which the E2E sign-in recognises its
    // success — it must not stand there while signed out.
    renderMitStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

    expect(screen.queryByRole("button", { name: "Mein Konto" })).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Einstellungen und Anmeldung" }),
    ).toBeInTheDocument();
  });

  it("has no gear ANYWHERE any more", async () => {
    for (const auth of [
      { status: "authenticated" as const, session: account(null) },
      { status: "anonymous" as const, session: null },
    ]) {
      const { unmount } = renderMitStore(<SiteHeader />, { auth });

      expect(
        screen.queryByRole("button", { name: /Darstellung und Sprache/i }),
      ).not.toBeInTheDocument();

      unmount();
    }
  });

  it("gives signed-out visitors appearance and language too — at the same place", async () => {
    // Whoever switches the language because they cannot read the UI must be
    // able to do so before signing in.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

    await user.click(screen.getByRole("button", { name: "Einstellungen und Anmeldung" }));

    expect(screen.getByRole("menuitem", { name: "Anmelden" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("offers no GitHub to an account in the crafts", async () => {
    await openAccountMenu("handwerk");

    expect(
      screen.queryByRole("menuitem", { name: "GitHub" }),
    ).not.toBeInTheDocument();

    // The rest of the menu stands.
    expect(
      screen.getByRole("menuitem", { name: "Lebenslauf" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("menuitem", { name: "Portfolio" }),
    ).toBeInTheDocument();
  });
});
