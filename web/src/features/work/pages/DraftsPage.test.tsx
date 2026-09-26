import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { forgetContext } from "../lib/letterContext";
import { DraftsPage } from "./DraftsPage";

const DRAFT = {
  id: "01a078bd-ec65-7b57-b444-d25c53edbcda",
  job_id: "11111111-1111-4111-8111-111111111111",
  subject: "ewerbung für die Stelle X",
  body: "Sehr geehrte Damen und Herren, langer Text der nicht auf der Karte stehen darf.",
  status: "failed",
  version: 1,
  error: "Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).",
  shares_resume: true,
  documents: [],
  comments: [],
  updated_at: "2026-09-07T00:00:00Z",
};

const SESSION = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    berufsfeld: null,
  },
};

type Answer = { status?: number; body?: unknown };

function stubFetch(
  routes: (url: string, init: RequestInit | undefined) => Answer | Promise<Answer>,
) {
  const spy = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const answer = await routes(url, init);
    const status = answer.status ?? 200;
    return new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), {
      status,
      headers: { "content-type": "application/json" },
    });
  });
  vi.stubGlobal("fetch", spy);
  return spy;
}

/**
 * Empty BEFORE every case, not only after.
 *
 * `vi.unstubAllGlobals()` in the `afterEach` runs before the component is
 * unmounted. Whatever the dying page still triggers afterwards hits the REAL
 * server — on a development machine the running stack, which answers 401 — and
 * writes that into the cache just emptied. The next case then reads it instead
 * of its own stub.
 */
beforeEach(() => {
  forgetContext();
});

afterEach(() => {
  // UNMOUNT FIRST, THEN PULL THE STUB. The other way round leaves a window: the
  // still mounted page triggers a fetch after `unstubAllGlobals`, it hits the
  // REAL server (on a development machine the running stack) and writes its
  // 401 into the cache, which the next case then reads instead of its own stub.
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("DraftsPage", () => {
  it("shows no letter text on the card and allows opening", async () => {
    stubFetch((url) =>
      url.includes("/applications/drafts") ? { body: [DRAFT] } : { status: 404 },
    );

    renderWithStore(<DraftsPage />, { auth: SESSION, route: "/applications/drafts" });

    expect(await screen.findByText("Fehlgeschlagen")).toBeInTheDocument();
    expect(screen.getByText(/zu lange nichts geliefert/i)).toBeInTheDocument();
    expect(screen.queryByText(/Sehr geehrte Damen und Herren/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Lies den Brief/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Öffnen/i })).toBeEnabled();
  });

  it("shows a timeout without an HTTP number", async () => {
    stubFetch((url) =>
      url.includes("/applications/drafts") ? { body: [DRAFT] } : { status: 404 },
    );

    renderWithStore(<DraftsPage />, { auth: SESSION, route: "/applications/drafts" });

    expect(await screen.findByText(/zu lange nichts geliefert/i)).toBeInTheDocument();
    expect(screen.queryByText(/402/)).not.toBeInTheDocument();
    expect(screen.queryByText(/404/)).not.toBeInTheDocument();
    expect(screen.queryByText(/422/)).not.toBeInTheDocument();
    expect(screen.queryByText(/nicht eingerichtet/i)).not.toBeInTheDocument();
  });

  it("goes to the detail page after writing again", async () => {
    stubFetch((url, init) => {
      if (url.includes("/write") && init?.method === "POST") {
        return { body: { ...DRAFT, status: "generating", error: null, body: "", subject: "" } };
      }
      if (url.includes("/applications/drafts")) {
        return { body: [DRAFT] };
      }
      return { status: 404 };
    });

    renderWithStore(<DraftsPage />, { auth: SESSION, route: "/applications/drafts" });
    expect(await screen.findByText("Fehlgeschlagen")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Noch einmal schreiben/i }));
  });
});
