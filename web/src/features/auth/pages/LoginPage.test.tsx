import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router-dom";

import { renderMitStore } from "../test/render";
import { LoginPage } from "./LoginPage";

type Antwort = { status?: number; body?: unknown };

/** Antwortet je Pfad. `new URL(...).pathname` statt `includes` — exakt. */
function antworten(karte: Record<string, Antwort>) {
  const asked: { path: string; body: string }[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = new URL(url).pathname;
      asked.push({ path, body: String(init?.body ?? "") });
      const answer = karte[path] ?? { body: {} };
      return new Response(JSON.stringify(answer.body ?? {}), {
        status: answer.status ?? 200,
      });
    }),
  );
  return asked;
}

const SITZUNG = {
  body: { user: { user_id: "u-1", email: "a@b.com", tenant_id: null } },
};

/** Die Seite mit einem Ziel, an dem sich eine Weiterleitung zeigen lässt. */
function seite() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/overview" element={<p>Übersicht</p>} />
    </Routes>
  );
}

beforeEach(() => {
  antworten({});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("LoginPage", () => {
  it("trägt die deutsche Überschrift", () => {
    renderMitStore(seite(), { route: "/login" });

    expect(
      screen.getByRole("heading", { name: "Anmelden" }),
    ).toBeInTheDocument();
  });

  it("fragt nicht nach einem Unternehmen", () => {
    // Ein Mandant ist ein Unternehmen, und eine Person hat keines (ADR-0017).
    // Jemanden eine Mandanten-UUID tippen zu lassen war falsch und unbenutzbar.
    renderMitStore(seite(), { route: "/login" });

    expect(screen.queryByLabelText(/Mandant/i)).toBeNull();
    expect(screen.queryByLabelText(/Unternehmen/i)).toBeNull();
    expect(screen.getByLabelText(/E-Mail/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Passwort/i)).toBeInTheDocument();
  });

  it("meldet an und führt danach auf die Übersicht", async () => {
    antworten({ "/auth/login": {}, "/auth/session": SITZUNG });
    const user = userEvent.setup();
    renderMitStore(seite(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByText("Übersicht")).toBeInTheDocument();
  });

  it("schickt E-Mail und Passwort, und sonst nichts", async () => {
    const asked = antworten({ "/auth/login": {}, "/auth/session": SITZUNG });
    const user = userEvent.setup();
    renderMitStore(seite(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    await screen.findByText("Übersicht");
    const signIn = asked.find((entry) => entry.path === "/auth/login");
    expect(signIn?.body).toContain('"email"');
    expect(signIn?.body).toContain('"password"');
    // Der Mandant kommt nie vom Client — er entsteht erst beim Wechsel in ein
    // Unternehmen, und dort entscheidet ihn der Server (ADR-0018).
    expect(signIn?.body).not.toContain("tenant");
  });

  it("zeigt den Fehlschlag als Meldung und leitet nicht weiter", async () => {
    antworten({
      "/auth/login": {
        status: 401,
        body: { detail: "E-Mail oder Passwort stimmen nicht." },
      },
    });
    const user = userEvent.setup();
    renderMitStore(seite(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "falsch");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    // E2E wartet auf genau diese Rolle.
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "E-Mail oder Passwort stimmen nicht.",
    );
    expect(screen.queryByText("Übersicht")).toBeNull();
  });

  /**
   * Der Fehlschlag OHNE `ApiError`, und der gefährlichste: der `login`-Thunk
   * `unwrap()`t intern `loadSession`. Scheitert der Teil, ist `action.payload`
   * undefiniert, der Slice setzt `error = null` — und der Knopf tut sichtbar
   * nichts. Ohne den eigenen Rückfalltext bliebe die Seite an genau der Stelle
   * stumm, an der jemand nicht weiterkommt.
   */
  it("bleibt nicht stumm, wenn die Sitzung nach dem Anmelden nicht zu lesen ist", async () => {
    antworten({
      "/auth/login": {},
      "/auth/session": { status: 500, body: {} },
    });
    const user = userEvent.setup();
    renderMitStore(seite(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Anmeldung fehlgeschlagen",
    );
    expect(screen.queryByText("Übersicht")).toBeNull();
  });

  it("sagt es, wenn die Anmeldung durchging und trotzdem niemand da ist", async () => {
    // 200 auf die Anmeldung, aber `/auth/session` kennt niemanden. Das ist kein
    // Erfolg — und wortlos auf dem Formular stehen zu bleiben erklärt es nicht.
    antworten({ "/auth/login": {}, "/auth/session": { body: { user: null } } });
    const user = userEvent.setup();
    renderMitStore(seite(), { route: "/login" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.click(screen.getByRole("button", { name: /Anmelden/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Sitzung");
    expect(screen.queryByText("Übersicht")).toBeNull();
  });

  it("bietet den anderen Weg auf der Seite an, nicht in der Kopfzeile", () => {
    renderMitStore(seite(), { route: "/login" });

    expect(screen.getByRole("tab", { name: "Konto anlegen" })).toHaveAttribute(
      "href",
      "/register",
    );
  });
});
