import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderWithStore } from "../../../features/auth/test/render";
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
      occupationalField,
    };
  }

  async function openAccountMenu(occupationalField: OccupationalFieldChoice) {
    const user = userEvent.setup();
    renderWithStore(<SiteHeader />, {
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
    renderWithStore(<SiteHeader />, {
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
    renderWithStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

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
      const { unmount } = renderWithStore(<SiteHeader />, { auth });

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
    renderWithStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

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

/**
 * Two roles, two bars: acting as a person one looks for work, acting for a
 * company one hires — and the account menu keeps the person's own pages to
 * the person.
 */
describe("SiteHeader by context", () => {
  const TENANT = "22222222-2222-4222-8222-222222222222";
  const anna = (tenantId: string | null): Session => ({
    userId: "11111111-1111-1111-1111-111111111111",
    email: "anna@example.com",
    tenantId,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  });

  const links = () =>
    screen.getAllByRole("link", { hidden: true }).map((link) => link.textContent ?? "");

  it("shows a person what a person does", () => {
    renderWithStore(<SiteHeader />, { auth: { status: "authenticated", session: anna(null) } });

    expect(links()).toEqual(
      expect.arrayContaining(["Stellen", "Bewerbungen", "Nachrichten", "Marktstatus", "Wechsel", "Berater"]),
    );
    expect(links()).not.toContain("Unsere Stellen");
    expect(screen.queryByRole("button", { name: "Unternehmen" })).not.toBeInTheDocument();
  });

  it("shows a company what a company does, and no Marktstatus", async () => {
    const user = userEvent.setup();
    renderWithStore(<SiteHeader />, {
      auth: {
        status: "authenticated",
        session: anna(TENANT),
        memberships: [{ id: TENANT, name: "Muster GmbH", role: "admin" }],
      },
    });

    expect(links()).toEqual(
      expect.arrayContaining(["Übersicht", "Unsere Stellen", "Bewerbungen", "Kandidatinnen", "Nachrichten", "Wechsel"]),
    );
    expect(links()).not.toContain("Marktstatus");

    await user.click(screen.getByRole("button", { name: "Unternehmen" }));
    expect(screen.getByRole("menuitem", { name: "Berater-Gespräche" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Team" })).toBeInTheDocument();
  });

  it("keeps the person's own pages out of the account menu while acting for a company", async () => {
    const user = userEvent.setup();
    renderWithStore(<SiteHeader />, { auth: { status: "authenticated", session: anna(TENANT) } });

    await user.click(screen.getByRole("button", { name: "Mein Konto" }));

    expect(screen.queryByRole("menuitem", { name: "Profil" })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Freigaben" })).not.toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Einstellungen" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Abmelden" })).toBeInTheDocument();
  });

  it("names the drafts what they are", async () => {
    const user = userEvent.setup();
    renderWithStore(<SiteHeader />, { auth: { status: "authenticated", session: anna(null) } });

    await user.click(screen.getByRole("button", { name: "Mein Konto" }));

    expect(screen.getByRole("menuitem", { name: "Bewerbungsentwürfe" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Arbeitsproben" })).toBeInTheDocument();
  });
});
