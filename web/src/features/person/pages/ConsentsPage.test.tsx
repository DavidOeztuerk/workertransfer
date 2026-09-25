import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SIGNED_IN, PERSON, renderWithStore } from "../test/render";
import { type Network, stubNetwork } from "../test/network";
import { ConsentsPage } from "./ConsentsPage";

const TENANT = "22222222-2222-2222-2222-222222222222";

function granted(capability: string) {
  return { capability, granted_at: "2026-08-02T10:00:00Z" };
}

let wire: Network;

beforeEach(() => {
  wire = stubNetwork({
    "GET /consent/me": { body: [] },
    "POST /consent/revoke": { body: { granted: false } },
    "POST /consent/grant": { body: { granted: true } },
    // The page now also asks whether a GitHub account is connected — without
    // one there is nothing to release there.
    "GET /github/me": { status: 404 },
    [`GET /companies/${TENANT}/profile`]: { status: 404 },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ConsentsPage", () => {
  it("asks for sign-in instead of showing releases that belong to nobody", async () => {
    renderWithStore(<ConsentsPage />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(wire.calls).toHaveLength(0);
  });

  it("draws a loading state while the session is unknown", async () => {
    // `unknown` means "not asked yet". Drawn as "signed out", a signed-in
    // person would see "Please sign in" on a cold start.
    renderWithStore(<ConsentsPage />, { auth: { status: "unknown" } });

    expect(await screen.findByRole("status")).toBeTruthy();
    expect(screen.queryByRole("link", { name: "anmelden" })).toBeNull();
  });

  it("says clearly that nothing is released when nothing is released", async () => {
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(await screen.findByText(/Niemand sieht etwas von dir/)).toBeTruthy();
  });

  it("reads its own list without a subject id — neither in the path nor in the query", async () => {
    // A foreign list would say which OTHER companies hold access.
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    await screen.findByText(/Niemand sieht etwas von dir/);
    const read = wire.last("GET /consent/me");
    expect(read?.url).toBe("/consent/me");
    expect(read?.url).not.toContain(PERSON.userId);
  });

  it("never shows an error as an empty list", async () => {
    // "You have released nothing" would be the most reassuring wrong answer
    // this system can give here.
    wire.set("GET /consent/me", {
      status: 503,
      body: { title: "Ledger silent" },
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText(/Niemand sieht etwas von dir/)).toBeNull();
  });

  it("names the area and says that a public release applies to everyone", async () => {
    // `market` instead of `profile`: the three global visibilities have become
    // switches at the top and therefore no longer stand as a row in the list.
    // What is checked is unchanged — how a public release is NAMED.
    wire.set("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(await screen.findByText(/Marktstatus · Alle Unternehmen/)).toBeTruthy();
  });

  it("resolves the company name instead of showing a UUID", async () => {
    wire.set("GET /consent/me", {
      body: [granted(`resume.visibility:tenant:${TENANT}`)],
    });
    wire.set(`GET /companies/${TENANT}/profile`, {
      body: { tenant_id: TENANT, display_name: "Acme GmbH" },
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(await screen.findByText(/Lebenslauf · Acme GmbH/)).toBeTruthy();
    expect(screen.queryByText(new RegExp(TENANT))).toBeNull();
  });

  it("invents no name for a company without a profile", async () => {
    wire.set("GET /consent/me", {
      body: [granted(`market.visibility:tenant:${TENANT}`)],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText(/Marktstatus · Ein Unternehmen/),
    ).toBeTruthy();
  });

  it("shows an unknown capability instead of swallowing it", async () => {
    // Hiding a release because the UI does not know its format would be the
    // worst conceivable defect on exactly this page.
    wire.set("GET /consent/me", {
      body: [granted("something.entirely:new")],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(await screen.findByText(/something.entirely:new/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Zurückziehen" })).toBeTruthy();
  });

  it("withdraws with a reason, because a revocation must be explainable", async () => {
    wire.set("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Zurückziehen" }),
    );

    await waitFor(() =>
      expect(wire.last("POST /consent/revoke")).toBeDefined(),
    );
    expect(wire.last("POST /consent/revoke")?.body).toEqual({
      subject_id: PERSON.userId,
      capability: "market.visibility:public",
      reason: expect.stringContaining("Freigaben"),
    });
  });

  /**
   * <strong>The page can GRANT now.</strong>
   *
   * Before, it was a one-way street: it showed what holds and offered
   * "Withdraw". Whoever wanted to become visible had to guess which of five
   * pages held the switch — a page called "My releases" that cannot grant a
   * release does not answer the question it is opened for.
   */
  it("grants a release instead of only being able to withdraw", async () => {
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    const toggle = await screen.findByRole("switch", {
      name: /Profil freigeben/,
    });
    expect(toggle).not.toBeChecked();

    await user.click(toggle);

    await waitFor(() =>
      expect(wire.last("POST /consent/grant")).toBeDefined(),
    );
    // Nobody asks for a reason when granting — only a withdrawal must be
    // explainable (ADR-0027).
    expect(wire.last("POST /consent/grant")?.body).toEqual({
      subject_id: PERSON.userId,
      capability: "profile.visibility:public",
    });
  });

  it("shows the switch as on when the release holds — and withdraws it with a reason", async () => {
    wire.set("GET /consent/me", {
      body: [granted("portfolio.visibility:public")],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    const toggle = await screen.findByRole("switch", {
      name: /Meine Arbeiten freigeben/,
    });
    expect(toggle).toBeChecked();

    await user.click(toggle);

    await waitFor(() =>
      expect(wire.last("POST /consent/revoke")).toBeDefined(),
    );
    expect(wire.last("POST /consent/revoke")?.body).toMatchObject({
      capability: "portfolio.visibility:public",
      reason: expect.stringContaining("Freigaben"),
    });
  });

  /**
   * The same release must not stand twice on the page.
   *
   * Once as a switch and once as a row with "Withdraw" — and nobody would know
   * which of the two holds.
   */
  it("does not additionally list a switched release as a row", async () => {
    wire.set("GET /consent/me", {
      body: [granted("profile.visibility:public")],
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    await screen.findByRole("switch", { name: /Profil freigeben/ });

    expect(screen.queryByText(/Profil · Alle Unternehmen/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Zurückziehen" })).toBeNull();
  });

  /**
   * Without a connected account there is nothing to release at GitHub — the
   * switch stays visible anyway and says what is missing.
   *
   * Hiding it would mean keeping the possibility quiet. Before this page the
   * switch did not exist at all: the service has always checked
   * `github.visibility:public`, but nobody could grant it anywhere.
   */
  it("says at GitHub that an account must be connected first", async () => {
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    const toggle = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });

    expect(toggle).toBeDisabled();
    expect(screen.getByText(/zuerst ein GitHub-Konto verbinden/)).toBeTruthy();
  });

  /**
   * A NAMED account is not evidence yet.
   *
   * Measured on the running system: the connection stood there as
   * <c>verified: false</c>, and the switch was operable. That would have
   * released the repositories of an account nobody had attributed to the
   * person.
   */
  it("does not let an unverified GitHub connection be released", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "octocat",
        verified: false,
        challenge_description: "workertransfer-verify-abc",
        fetched_at: null,
        repositories: [],
      },
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    const toggle = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });

    expect(toggle).toBeDisabled();
    expect(screen.getByText(/noch nicht nachgewiesen/)).toBeTruthy();
  });

  it("lets GitHub be released as soon as an account is connected", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [],
      },
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    const toggle = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });
    await waitFor(() => expect(toggle).not.toBeDisabled());

    await user.click(toggle);

    await waitFor(() =>
      expect(wire.last("POST /consent/grant")?.body).toMatchObject({
        capability: "github.visibility:public",
      }),
    );
  });

  /**
   * What has NO switch stands on the page anyway — and BOTH ways there do.
   *
   * Without the first paragraph the page would look complete and would not be:
   * the CV is missing, and whoever looks for it takes that for an oversight. It
   * is none (ADR-0020).
   *
   * Without the SECOND only half stood there, and it read wrongly: whoever was
   * just about to apply found a sentence about their current employer and
   * concluded their CV may go nowhere. Whoever applies sends it along — that is
   * the point. Both paragraphs must stand there, or the page explains one half
   * and confuses about the rest.
   */
  it("explains both ways to the CV — the requested and the sent one", async () => {
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText(/Wenn ein Unternehmen fragt/),
    ).toBeTruthy();
    expect(
      screen.getByText(/Wenn du dich selbst bewirbst, schickst du ihn mit/),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", { name: /Zu meinem Lebenslauf/ }),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", { name: /Zu meinen Bewerbungen/ }),
    ).toBeTruthy();
  });

  it("reports a failed withdrawal instead of faking success", async () => {
    wire.set("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    wire.set("POST /consent/revoke", {
      status: 503,
      body: { title: "No connection to the consent ledger." },
    });
    renderWithStore(<ConsentsPage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Zurückziehen" }),
    );

    expect(await screen.findByRole("alert")).toBeTruthy();
  });
});
