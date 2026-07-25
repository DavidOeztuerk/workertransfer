import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { LoginRoute } from "./login";

afterEach(() => vi.restoreAllMocks());

// jsdom's window.location is not writable by default; replace it with a stub
// whose `href` is a real getter/setter so the redirect branch
// (`window.location.href = "/"`) is captured without performing real navigation.
function stubLocation() {
  let href = "";
  const stub = { get href() { return href; }, set href(v: string) { href = v; } };
  Object.defineProperty(window, "location", {
    value: stub,
    writable: true,
    configurable: true,
  });
  return { get href() { return href; } };
}

describe("LoginRoute", () => {
  it("renders the German heading", () => {
    render(<LoginRoute />);
    expect(screen.getByRole("heading", { name: "Anmelden" })).toBeInTheDocument();
  });

  it("submits the form and redirects on a successful login", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ status: "ok" }), { status: 200 }))
    );
    const location = stubLocation();

    render(<LoginRoute />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(
      screen.getByLabelText("Mandant-ID"),
      "11111111-1111-1111-1111-111111111111"
    );
    await user.click(screen.getByRole("button", { name: "Anmelden" }));

    // Successful login sets the redirect target; the form did not throw and the
    // fetch stub was called exactly once.
    expect(location.href).toBe("/");
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
  });

  it("shows the error message on a failed login and does not redirect", async () => {
    // 401 with an unparseable body: login() keeps its default German message
    // (the detail-passthrough branch is covered by auth/client.test.ts).
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("not json", { status: 401 }))
    );
    const location = stubLocation();

    render(<LoginRoute />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "wrong");
    await user.type(
      screen.getByLabelText("Mandant-ID"),
      "11111111-1111-1111-1111-111111111111"
    );
    await user.click(screen.getByRole("button", { name: "Anmelden" }));

    await screen.findByRole("alert");
    expect(screen.getByText("Anmeldung fehlgeschlagen")).toBeInTheDocument();
    expect(location.href).toBe("");
  });
});
