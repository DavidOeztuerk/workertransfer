import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router-dom";

import { renderWithStore } from "../test/render";
import { LoginPage } from "./LoginPage";

type Answer = { status?: number; body?: unknown };

/** Answers per path. `new URL(...).pathname` instead of `includes` — exact. */
function answer(byPath: Record<string, Answer>) {
  const asked: { path: string; body: string }[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = new URL(url).pathname;
      asked.push({ path, body: String(init?.body ?? "") });
      const reply = byPath[path] ?? { body: {} };
      return new Response(JSON.stringify(reply.body ?? {}), {
        status: reply.status ?? 200,
      });
    }),
  );
  return asked;
}

const SESSION = {
  body: { user: { user_id: "u-1", email: "a@b.com", tenant_id: null } },
};

/** The page with a target at which a redirect can show itself. */
function page() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/overview" element={<p>Overview target</p>} />
    </Routes>
  );
}

beforeEach(() => {
  answer({});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("LoginPage", () => {
  it("carries the German heading", () => {
    renderWithStore(page(), { route: "/login" });

    expect(
      screen.getByRole("heading", { name: "Anmelden" }),
    ).toBeInTheDocument();
  });

  it("does not ask for a company", () => {
    // A tenant is a company, and a person has none (ADR-0017). Making someone
    // type a tenant UUID was wrong and unusable.
    renderWithStore(page(), { route: "/login" });

    expect(screen.queryByLabelText(/Mandant/i)).toBeNull();
    expect(screen.queryByLabelText(/Unternehmen/i)).toBeNull();
    expect(screen.getByLabelText(/E-Mail/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Passwort/i)).toBeInTheDocument();
  });

  it("signs in and then leads to the overview", async () => {
    answer({ "/auth/login": {}, "/auth/session": SESSION });
    const user = userEvent.setup();
    renderWithStore(page(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByText("Overview target")).toBeInTheDocument();
  });

  it("sends email and password, and nothing else", async () => {
    const asked = answer({ "/auth/login": {}, "/auth/session": SESSION });
    const user = userEvent.setup();
    renderWithStore(page(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    await screen.findByText("Overview target");
    const signIn = asked.find((entry) => entry.path === "/auth/login");
    expect(signIn?.body).toContain('"email"');
    expect(signIn?.body).toContain('"password"');
    // The tenant never comes from the client — it only comes into being on the
    // switch into a company, and there the server decides it (ADR-0018).
    expect(signIn?.body).not.toContain("tenant");
  });

  it("shows the failure as a message and does not redirect", async () => {
    answer({
      "/auth/login": {
        status: 401,
        body: { detail: "E-Mail oder Passwort stimmen nicht." },
      },
    });
    const user = userEvent.setup();
    renderWithStore(page(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "wrong-password");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    // E2E waits for exactly this role.
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "E-Mail oder Passwort stimmen nicht.",
    );
    expect(screen.queryByText("Overview target")).toBeNull();
  });

  /**
   * The failure WITHOUT an `ApiError`, and the most dangerous one: the `login`
   * thunk `unwrap()`s `loadSession` internally. If that part fails,
   * `action.payload` is undefined, the slice sets `error = null` — and the
   * button visibly does nothing. Without its own fallback text the page would
   * stay silent at exactly the place where someone cannot get on.
   */
  it("does not stay silent when the session cannot be read after signing in", async () => {
    answer({
      "/auth/login": {},
      "/auth/session": { status: 500, body: {} },
    });
    const user = userEvent.setup();
    renderWithStore(page(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Anmeldung fehlgeschlagen",
    );
    expect(screen.queryByText("Overview target")).toBeNull();
  });

  it("says so when the sign-in went through and still nobody is there", async () => {
    // 200 on the sign-in, but `/auth/session` knows nobody. That is no success —
    // and staying on the form without a word does not explain it.
    answer({ "/auth/login": {}, "/auth/session": { body: { user: null } } });
    const user = userEvent.setup();
    renderWithStore(page(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Sitzung");
    expect(screen.queryByText("Overview target")).toBeNull();
  });

  it("offers the other path on the page, not in the header", () => {
    renderWithStore(page(), { route: "/login" });

    expect(screen.getByRole("tab", { name: "Konto anlegen" })).toHaveAttribute(
      "href",
      "/register",
    );
  });
});
