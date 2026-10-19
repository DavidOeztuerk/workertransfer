import { act, cleanup, fireEvent, screen } from "@testing-library/react";
import { useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../../../features/work/test/render";
import { notifySessionExpired } from "../../../core/api/client";
import { SessionGate } from "./SessionGate";

function Where() {
  return <p data-testid="where">{useLocation().pathname}</p>;
}

/**
 * The session is renewed while somebody is here — and only then.
 *
 * Until 06.10.2026 a timer renewed it unconditionally, so an open, untouched
 * tab stayed signed in for the whole absolute lifetime and "eight hours
 * without use" (docs/VISION-FRAGEN.md, K) never came.
 */

const PERSON = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const THIRTEEN_MINUTES = 13 * 60 * 1000;

describe("the session gate", () => {
  let sessionChecks = 0;

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: false });
    sessionChecks = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: string | URL | Request) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.includes("/auth/session") || url.endsWith("/me")) sessionChecks++;
        return new Response("null", { status: 200, headers: { "content-type": "application/json" } });
      }),
    );
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("does not renew an untouched tab", async () => {
    renderWithStore(<SessionGate>page</SessionGate>, { auth: PERSON, route: "/overview" });
    const before = sessionChecks;

    await act(async () => {
      vi.advanceTimersByTime(THIRTEEN_MINUTES + 1000);
    });

    expect(sessionChecks).toBe(before);
  });

  it("renews when somebody was active", async () => {
    renderWithStore(<SessionGate>page</SessionGate>, { auth: PERSON, route: "/overview" });
    const before = sessionChecks;

    await act(async () => {
      vi.advanceTimersByTime(THIRTEEN_MINUTES - 60 * 1000);
    });
    fireEvent.keyDown(window, { key: "a" });
    await act(async () => {
      vi.advanceTimersByTime(60 * 1000 + 1000);
    });

    expect(sessionChecks).toBeGreaterThan(before);
  });

  it("leaves a protected page when the session is dead", async () => {
    renderWithStore(<SessionGate><Where /></SessionGate>, { auth: PERSON, route: "/overview" });

    await act(async () => notifySessionExpired());

    expect(screen.getByTestId("where")).toHaveTextContent(/^\/$/);
  });

  /**
   * A public page stays. The confirmation page of a new account, opened in a
   * browser that still held a revoked cookie, was sent to the start page before
   * the person could read that it worked (measured 07.10.2026).
   */
  it("keeps a public page when the session is dead", async () => {
    // A new tab: nobody known yet, a dead cookie in the jar.
    renderWithStore(<SessionGate><Where /></SessionGate>, {
      auth: { status: "unknown", session: null },
      route: "/verify",
    });

    await act(async () => notifySessionExpired());

    expect(screen.getByTestId("where")).toHaveTextContent("/verify");
  });
});
