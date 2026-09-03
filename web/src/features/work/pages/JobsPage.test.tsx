import userEvent from "@testing-library/user-event";
import { screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderMitStore } from "../test/render";
import { JobsPage } from "./JobsPage";

/**
 * Gestubbt wird `fetch`, nicht das Client-Modul.
 *
 * Die alten Tests mockten `../jobs/client` — damit prüften sie die Seite gegen
 * eine Erfindung. Der Draht-Fund (`19f45b6`: camelCase kam beim Server nicht an)
 * versteckte sich genau in der Schicht, die dabei übersprungen wurde. Hier
 * laufen Pfad, Methode und snake_case durch den echten Client.
 */
type Antwort = { status?: number; body?: unknown };

function stubFetch(
  routen: (url: string, init: RequestInit | undefined) => Antwort,
) {
  const spion = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = routen(url, init);
      const status = answer.status ?? 200;
      return new Response(
        status === 204 ? null : JSON.stringify(answer.body ?? null),
        {
          status,
          headers: { "content-type": "application/json" },
        },
      );
    },
  );
  vi.stubGlobal("fetch", spion);
  return spion;
}

const STELLE = {
  id: "11111111-1111-4111-8111-111111111111",
  tenant_id: "22222222-2222-4222-8222-222222222222",
  title: "Backend-Entwicklung",
  description: "Wir bauen Dienste.",
  location: "Berlin",
  remote: "hybrid",
  employment: "full_time",
  skills: ["Python", "Kubernetes", "Go"],
  status: "published",
  published_at: "2026-08-01T00:00:00Z",
  updated_at: "2026-08-01T00:00:00Z",
};

const SITZUNG = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null, language: "de",
  },
};

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("JobsPage", () => {
  it("fragt /jobs und zeigt, was das Unternehmen geschrieben hat", async () => {
    const spion = stubFetch((url) => {
      if (url.includes("/jobs"))
        return { body: { items: [STELLE], next_cursor: null } };
      if (url.includes("/companies/")) {
        return {
          body: {
            tenant_id: STELLE.tenant_id,
            slug: "acme",
            display_name: "Acme GmbH",
            about: "",
            website: null,
            locations: [],
            benefits: [],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      return { status: 404 };
    });

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("Backend-Entwicklung")).toBeInTheDocument();
    expect(await screen.findByText("Acme GmbH")).toBeInTheDocument();
    expect(
      spion.mock.calls.some(([url]) => String(url).endsWith("/jobs")),
    ).toBe(true);
  });

  it("schickt leere Filter gar nicht erst mit — ein `remote=` fände nichts", async () => {
    const spion = stubFetch(() => ({ body: { items: [], next_cursor: null } }));

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.type(screen.getByLabelText(/Suchbegriff/i), "Python");
    await userEvent.click(screen.getByRole("button", { name: /Suchen/i }));

    await waitFor(() => {
      const urls = spion.mock.calls.map(([url]) => String(url));
      expect(urls.some((url) => url.includes("q=Python"))).toBe(true);
    });
    expect(
      spion.mock.calls.every(([url]) => !String(url).includes("remote=")),
    ).toBe(true);
  });

  it("nennt beim leeren Ergebnis, dass nichts gefunden wurde", async () => {
    stubFetch(() => ({ body: { items: [], next_cursor: null } }));
    renderMitStore(<JobsPage />);
    expect(await screen.findByText(/nichts gefunden/i)).toBeInTheDocument();
  });

  it("zeigt ohne Anmeldung keine Passung — und behauptet damit keine Lücke", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: { items: [STELLE], next_cursor: null } }
        : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");

    expect(
      screen.queryByText(/von 3 genannten Fähigkeiten/),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
    // Die Fähigkeiten selbst stehen trotzdem da — sie gehören der Anzeige.
    expect(screen.getByText("Python")).toBeInTheDocument();
  });

  it("zählt die Häkchen, nennt aber nie einen Prozentwert", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SITZUNG.session.userId,
            headline: "",
            bio: "",
            location: "",
            remote_ok: false,
            skills: ["python", "Kubernetes"],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      if (url.includes("/jobs"))
        return { body: { items: [STELLE], next_cursor: null } };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });

    expect(
      await screen.findByText("Du hast 2 von 3 genannten Fähigkeiten:"),
    ).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
    // Gleichheit, nicht Enthaltensein: "Go" bleibt eine Lücke.
    expect(screen.getByText("Go").closest("li")).toHaveAttribute(
      "data-match",
      "missing",
    );
  });

  it("sagt „trage Fähigkeiten ein“ statt „0 von 3“, wenn nichts eingetragen ist", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SITZUNG.session.userId,
            headline: "",
            bio: "",
            location: "",
            remote_ok: false,
            skills: [],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      if (url.includes("/jobs"))
        return { body: { items: [STELLE], next_cursor: null } };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });

    expect(
      await screen.findByText(/Trage Fähigkeiten in deinem/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
  });

  it("merkt sich beim Bewerben ohne Konto nur die UUID, nie einen Pfad", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: { items: [STELLE], next_cursor: null } }
        : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: "Bewerben" }));

    const gemerkt = JSON.parse(
      window.localStorage.getItem("wt.gemerkte-stelle") ?? "{}",
    ) as Record<string, unknown>;
    expect(gemerkt.jobId).toBe(STELLE.id);
    expect(JSON.stringify(gemerkt)).not.toContain("/");
  });

  it("blättert weiter und behält die vorige Seite", async () => {
    const zweite = {
      ...STELLE,
      id: "44444444-4444-4444-8444-444444444444",
      title: "Zweite Stelle",
    };
    stubFetch((url) => {
      if (!url.includes("/jobs")) return { status: 404 };
      return url.includes("cursor=n2")
        ? { body: { items: [zweite], next_cursor: null } }
        : { body: { items: [STELLE], next_cursor: "n2" } };
    });

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /Mehr laden/i }));

    expect(await screen.findByText("Zweite Stelle")).toBeInTheDocument();
    expect(screen.getByText("Backend-Entwicklung")).toBeInTheDocument();
  });

  it("zeigt einen gescheiterten Abruf als Fehler, nicht als leere Liste", async () => {
    stubFetch(() => ({
      status: 500,
      body: {
        title: "Kaputt",
        detail: "Es ging schief.",
        correlationId: "abc-123",
      },
    }));

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("Es ging schief.")).toBeInTheDocument();
    expect(
      screen.queryByText("Dazu wurde nichts gefunden."),
    ).not.toBeInTheDocument();
    // Die Korrelationskennung ist der einzige Faden zurück durch alle Dienste.
    expect(screen.getByText(/abc-123/)).toBeInTheDocument();
  });
});
