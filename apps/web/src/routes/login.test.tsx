import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithProviders } from "../test/render";
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
    renderWithProviders(<LoginRoute />);
    expect(screen.getByRole("heading", { name: "Anmelden" })).toBeInTheDocument();
  });

  it("submits the form and redirects on a successful login", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ status: "ok" }), { status: 200 }))
    );
    const location = stubLocation();

    renderWithProviders(<LoginRoute />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.click(screen.getByRole("button", { name: "Anmelden" }));

    // Successful login sets the redirect target; the form did not throw and the
    // fetch stub was called exactly once.
    expect(location.href).toBe("/");
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
  });

  it("führt nach dem Anmelden auf die Bewerbungsseite der gemerkten Stelle", async () => {
    // Diese Zeile war von keinem Test gedeckt, und das Ziel hat sich geändert:
    // vorher `/jobs?stelle=<id>` — eine gefilterte Liste, die eine Box
    // aufklappte —, jetzt die eigene Adresse des Formulars. Ohne diesen Test
    // hätte man den Weg zurück verstellen können, ohne dass etwas rot wird.
    const stelle = "11111111-2222-3333-4444-555555555555";
    window.localStorage.setItem(
      "wt.gemerkte-stelle",
      JSON.stringify({ jobId: stelle, titel: "Backend-Entwicklerin", gemerktAm: Date.now() })
    );
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ status: "ok" }), { status: 200 }))
    );
    const location = stubLocation();

    renderWithProviders(<LoginRoute />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.click(screen.getByRole("button", { name: "Anmelden" }));

    expect(location.href).toBe(`/jobs/${stelle}/apply`);
    // Und die Absicht ist verbraucht: sonst käme sie beim nächsten Anmelden
    // wieder, und niemand wüsste, warum.
    expect(window.localStorage.getItem("wt.gemerkte-stelle")).toBeNull();
  });

  it("shows the error message on a failed login and does not redirect", async () => {
    // 401 with an unparseable body: login() keeps its default German message
    // (the detail-passthrough branch is covered by auth/client.test.ts).
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("not json", { status: 401 }))
    );
    const location = stubLocation();

    renderWithProviders(<LoginRoute />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "wrong");
    await user.click(screen.getByRole("button", { name: "Anmelden" }));

    await screen.findByRole("alert");
    expect(screen.getByText("Anmeldung fehlgeschlagen")).toBeInTheDocument();
    expect(location.href).toBe("");
  });

  it("does not ask the user for a company id", () => {
    // A tenant is a company and a person has none (ADR-0017). Making someone
    // type a company UUID to log in was both wrong and unusable.
    renderWithProviders(<LoginRoute />);

    expect(screen.queryByLabelText("Mandant-ID")).toBeNull();
    expect(screen.getByLabelText("E-Mail")).toBeInTheDocument();
    expect(screen.getByLabelText("Passwort")).toBeInTheDocument();
  });
});
