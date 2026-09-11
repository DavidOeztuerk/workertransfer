import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderMitStore } from "../test/render";
import { vergissKontext } from "../lib/kontext";
import { DraftsPage } from "./DraftsPage";

const ENTWURF = {
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

const SITZUNG = {
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

type Antwort = { status?: number; body?: unknown };

function stubFetch(routen: (url: string, init: RequestInit | undefined) => Antwort | Promise<Antwort>) {
  const spion = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const answer = await routen(url, init);
    const status = answer.status ?? 200;
    return new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), {
      status,
      headers: { "content-type": "application/json" },
    });
  });
  vi.stubGlobal("fetch", spion);
  return spion;
}

/**
 * VOR jedem Fall leeren, nicht nur danach.
 *
 * `vi.unstubAllGlobals()` im `afterEach` laeuft, bevor die Komponente
 * ausgehaengt ist. Was die sterbende Seite danach noch anstoesst, trifft den
 * ECHTEN Server — im Entwicklungsrechner den laufenden Stapel, der mit 401
 * antwortet — und schreibt das in den gerade geleerten Zwischenspeicher. Der
 * naechste Fall liest es dann statt seines eigenen Stubs.
 */
beforeEach(() => {
  vergissKontext();
});

afterEach(() => {
  // ERST AUSHAENGEN, DANN DEN STUB ZIEHEN. Andersherum bleibt ein Fenster:
  // die noch montierte Seite stoesst nach `unstubAllGlobals` einen Abruf an,
  // der trifft den ECHTEN Server (auf einem Entwicklungsrechner den laufenden
  // Stapel) und schreibt dessen 401 in den Zwischenspeicher, den der naechste
  // Fall dann liest statt seines eigenen Stubs.
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("DraftsPage", () => {
  it("zeigt keinen Brieftext auf der Karte und lässt Öffnen zu", async () => {
    stubFetch((url) =>
      url.includes("/applications/drafts") ? { body: [ENTWURF] } : { status: 404 },
    );

    renderMitStore(<DraftsPage />, { auth: SITZUNG, route: "/applications/drafts" });

    expect(await screen.findByText("Fehlgeschlagen")).toBeInTheDocument();
    expect(screen.getByText(/zu lange nichts geliefert/i)).toBeInTheDocument();
    expect(screen.queryByText(/Sehr geehrte Damen und Herren/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Lies den Brief/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Öffnen/i })).toBeEnabled();
  });

  it("zeigt eine Zeitüberschreitung ohne HTTP-Zahl", async () => {
    stubFetch((url) =>
      url.includes("/applications/drafts") ? { body: [ENTWURF] } : { status: 404 },
    );

    renderMitStore(<DraftsPage />, { auth: SITZUNG, route: "/applications/drafts" });

    expect(await screen.findByText(/zu lange nichts geliefert/i)).toBeInTheDocument();
    expect(screen.queryByText(/402/)).not.toBeInTheDocument();
    expect(screen.queryByText(/404/)).not.toBeInTheDocument();
    expect(screen.queryByText(/422/)).not.toBeInTheDocument();
    expect(screen.queryByText(/nicht eingerichtet/i)).not.toBeInTheDocument();
  });

  it("geht nach noch einmal schreiben auf die Detailseite", async () => {
    stubFetch((url, init) => {
      if (url.includes("/write") && init?.method === "POST") {
        return { body: { ...ENTWURF, status: "generating", error: null, body: "", subject: "" } };
      }
      if (url.includes("/applications/drafts")) {
        return { body: [ENTWURF] };
      }
      return { status: 404 };
    });

    renderMitStore(<DraftsPage />, { auth: SITZUNG, route: "/applications/drafts" });
    expect(await screen.findByText("Fehlgeschlagen")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Noch einmal schreiben/i }));
  });
});
