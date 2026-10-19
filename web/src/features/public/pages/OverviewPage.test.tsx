import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { OverviewPage } from "./OverviewPage";

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TENANT = "22222222-2222-2222-2222-222222222222";

interface TransferRow {
  status: string;
  requires_release: boolean;
  release_confirmed: boolean;
}

function transfer(values: Partial<TransferRow> = {}): TransferRow {
  return {
    status: "interested",
    requires_release: false,
    release_confirmed: false,
    ...values,
  };
}

/**
 * The four reads, answered by path.
 *
 * `endsWith` and not `includes`: `/transfers` is a substring of
 * `/transfers/me`, and with `includes` one's own list would get the answer of
 * the company list — a test that is then green without proving anything.
 */
type Answer = { status?: number; body?: unknown };

function answer(byPath: Record<string, Answer>) {
  const asked: string[] = [];
  const stub = vi.fn(async (url: string) => {
    const path = new URL(url).pathname;
    asked.push(path);
    const reply = byPath[path] ?? { body: [] };
    return new Response(JSON.stringify(reply.body ?? []), {
      status: reply.status ?? 200,
    });
  });
  vi.stubGlobal("fetch", stub);
  return asked;
}

const SIGNED_IN = {
  status: "authenticated" as const,
  session: { userId: SUBJECT, email: "anna@example.com", tenantId: null, language: "de", displayName: "Anna Example", givenName: "", familyName: "", occupationalField: null },
};

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: SUBJECT,
    email: "anna@example.com",
    tenantId: TENANT,
    language: "de", displayName: "Anna Example", givenName: "", familyName: "", occupationalField: null,
  },
};

beforeEach(() => {
  answer({});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("OverviewPage", () => {
  it("says clearly when nothing is waiting right now", async () => {
    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText("Gerade wartet nichts auf dich."),
    ).toBeInTheDocument();
  });

  it("counts only what waits for a decision, not what runs by itself", async () => {
    // An overview that also shows what runs by itself is a list — and lists get
    // overlooked.
    answer({
      "/transfers/me": {
        body: [
          transfer({ status: "interested" }), // waits for me
          transfer({ status: "talking" }), // running, the company is to move
          transfer({ status: "completed" }), // over
        ],
      },
    });

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText("1 Wechsel wartet auf dich"),
    ).toBeInTheDocument();
  });

  it("counts an outstanding release, not a confirmed one", async () => {
    answer({
      "/transfers/me": {
        body: [
          transfer({ status: "accepted", requires_release: true }),
          transfer({
            status: "accepted",
            requires_release: true,
            release_confirmed: true,
          }),
        ],
      },
    });

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText("1 Wechsel wartet auf dich"),
    ).toBeInTheDocument();
  });

  it("counts open requests of both kinds separately", async () => {
    answer({
      "/market/me/requests": {
        body: [{ status: "PENDING" }, { status: "GRANTED" }],
      },
      "/resumes/me/requests": { body: [{ status: "PENDING" }] },
    });

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText(
        "1 Unternehmen möchte sehen, ob du ansprechbar bist",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText("1 Anfrage nach deinem Lebenslauf"),
    ).toBeInTheDocument();
  });

  it("does not ask for company transfers without an active company", async () => {
    // Without a company the service answers 403 — and an error the request
    // itself produced would mark the overview as incomplete.
    const asked = answer({});

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    await screen.findByText("Gerade wartet nichts auf dich.");
    expect(asked).not.toContain("/transfers");
    expect(asked).toContain("/transfers/me");
  });

  it("keeps the company's business apart from the person's", async () => {
    answer({ "/transfers": { body: [transfer({ status: "talking" })] } });

    renderWithStore(<OverviewPage />, { auth: FOR_COMPANY });

    expect(
      await screen.findByText("1 Wechsel wartet auf euch"),
    ).toBeInTheDocument();
    expect(screen.getByText("Für dein Unternehmen")).toBeInTheDocument();
    expect(screen.queryByText("Für dich")).toBeNull();
  });

  it("admits an incomplete picture instead of claiming nothing waits", async () => {
    // "Nothing is pending" is the one statement that can be false after a
    // failed read — and it lulls into safety.
    answer({
      "/market/me/requests": {
        status: 503,
        body: { detail: "consent ledger silent" },
      },
    });

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByText("Gerade wartet nichts auf dich.")).toBeNull();
  });

  it("uses singular and plural, because a '1 Gespräche' looks broken", async () => {
    answer({
      "/transfers/me": {
        body: [
          transfer({ status: "interested" }),
          transfer({ status: "offered" }),
        ],
      },
    });

    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByText("2 Wechsel warten auf dich"),
    ).toBeInTheDocument();
  });

  /**
   * Signed out, "nichts wartet auf dich" is no empty overview but a false
   * statement: it says nothing about the person, only about the missing token.
   */
  it("shows a signed-out visitor no empty overview", async () => {
    const asked = answer({});

    renderWithStore(<OverviewPage />, { auth: { status: "anonymous" } });

    expect(screen.queryByText("Gerade wartet nichts auf dich.")).toBeNull();
    expect(screen.queryByRole("heading", { name: "Was liegt an" })).toBeNull();
    expect(asked).toHaveLength(0);
  });

  it("points a new account at its first step: the profile", async () => {
    answer({ "/profiles/me": { status: 404, body: { title: "Not found" } } });
    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    expect(await screen.findByRole("link", { name: "Profil anlegen" })).toHaveAttribute("href", "/profile");
  });

  it("says nothing about a profile it could not read", async () => {
    answer({ "/profiles/me": { status: 503, body: { title: "Unavailable" } } });
    renderWithStore(<OverviewPage />, { auth: SIGNED_IN });

    await screen.findByText("Gerade wartet nichts auf dich.");
    expect(screen.queryByRole("link", { name: "Profil anlegen" })).not.toBeInTheDocument();
  });

  it("does not ask a company about a person's profile", async () => {
    const asked = answer({ "/profiles/me": { status: 404, body: {} } });
    renderWithStore(<OverviewPage />, { auth: FOR_COMPANY });

    await screen.findByText("Gerade wartet nichts auf dich.");
    expect(asked).not.toContain("/profiles/me");
  });
});
