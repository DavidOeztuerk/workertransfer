import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { ExternalSignIn } from "./ExternalSignIn";
import { SignInWays } from "./SignInWays";

/**
 * Signing in with a provider (ADR-0053): a button only where the server can
 * keep it, the outcome of a returning flow in German, and the last way in
 * that stays.
 */

type Answer = { status?: number; body?: unknown };
type Call = { path: string; method: string };

function answer(byPath: Record<string, Answer>): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = new URL(url).pathname;
      const method = init?.method ?? "GET";
      calls.push({ path, method });
      const reply = byPath[`${method} ${path}`] ?? { body: {} };
      return new Response(reply.status === 204 ? null : JSON.stringify(reply.body ?? {}), {
        status: reply.status ?? 200,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("ExternalSignIn", () => {
  it("draws no button when the server has no provider", async () => {
    const calls = answer({ "GET /auth/external": { body: { providers: [] } } });
    renderWithStore(<ExternalSignIn />, { route: "/login" });

    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(0));
    expect(screen.queryByRole("link", { name: /anmelden/ })).toBeNull();
  });

  it("draws one button per provider, leading to the service", async () => {
    answer({ "GET /auth/external": { body: { providers: ["google", "apple"] } } });
    renderWithStore(<ExternalSignIn />, { route: "/login" });

    const google = await screen.findByRole("link", { name: "Mit Google anmelden" });
    expect(google).toHaveAttribute("href", expect.stringMatching(/\/auth\/external\/google\/start\?intent=signin$/));
    expect(screen.getByRole("link", { name: "Mit Apple anmelden" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Microsoft/ })).toBeNull();
    expect(screen.getByText(/ein Konto für dich als Person/)).toBeInTheDocument();
  });

  it("says why an address with an account was not linked", async () => {
    answer({ "GET /auth/external": { body: { providers: ["google"] } } });
    renderWithStore(<ExternalSignIn />, { route: "/login?external=address_taken" });

    expect(await screen.findByText(/Zu dieser Adresse gibt es schon ein Konto/)).toBeInTheDocument();
  });

  it("ignores a word nobody sends", async () => {
    answer({ "GET /auth/external": { body: { providers: [] } } });
    renderWithStore(<ExternalSignIn />, { route: "/login?external=<script>" });

    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("SignInWays", () => {
  const LINKS = {
    has_password: false,
    linked: [{ provider: "google", created_at: "2026-10-18T10:00:00Z" }],
    available: ["google", "microsoft"],
  };

  it("shows the linked providers and offers the others, signed in", async () => {
    answer({ "GET /auth/external/links": { body: LINKS } });
    renderWithStore(<SignInWays />, { route: "/settings" });

    expect(await screen.findByText(/Google, verbunden am/)).toBeInTheDocument();
    expect(screen.getByText(/Passwort: keines/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Microsoft verbinden" }))
      .toHaveAttribute("href", expect.stringMatching(/\/auth\/external\/microsoft\/start\?intent=link$/));
    expect(screen.queryByRole("link", { name: "Google verbinden" })).toBeNull();
  });

  it("keeps the last way in and says why", async () => {
    answer({
      "GET /auth/external/links": { body: LINKS },
      "DELETE /auth/external/links/google": {
        status: 409,
        body: { title: "Request failed", detail: "the last way to sign in stays", correlationId: "c" },
      },
    });
    renderWithStore(<SignInWays />, { route: "/settings" });

    await userEvent.click(await screen.findByRole("button", { name: "Trennen" }));

    expect(await screen.findByText(/Das ist dein einziger Anmeldeweg/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/last way to sign in/);
  });

  it("is not there when the server has no provider and nothing is linked", async () => {
    const calls = answer({ "GET /auth/external/links": { body: { has_password: true, linked: [], available: [] } } });
    renderWithStore(<SignInWays />, { route: "/settings" });

    await vi.waitFor(() => expect(calls.length).toBeGreaterThan(0));
    expect(screen.queryByText("Anmeldewege")).toBeNull();
  });
});
