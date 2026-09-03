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

/**
 * Eine Stelle, WIE DER DIENST SIE SCHICKT — nicht, wie die Oberfläche sie hält.
 *
 * <strong>Hier stand einmal `remote` und `employment`.</strong> Der Dienst
 * schreibt aber `remote_mode` und `employment_type`. Die ganze Reihe war grün,
 * während die Karte in der laufenden Anlage
 * `stelle.remoteundefined · stelle.employmentundefined` zeigte — weil der Test
 * eine Gestalt erfand, die es auf dem Draht nie gab, und `searchJobs` die
 * Antwort ungeprüft auf `Job` castete.
 *
 * Die Regel dahinter steht in CLAUDE.md beim Benachrichtigungsdraht: einen
 * Namen auf beiden Seiten selbst zu schreiben ist eine Zeichenkette, die man
 * zweimal prüft. Ein Fixture muss den DRAHT nachbilden.
 *
 * Und vollständig gefüllt, jedes Feld: ein Fixture mit leeren Feldern lässt
 * genau die Zweige ungeprüft, die etwas anzeigen.
 */
const STELLE = {
  id: "11111111-1111-4111-8111-111111111111",
  tenant_id: "22222222-2222-4222-8222-222222222222",
  title: "Backend-Entwicklung",
  description: "Wir bauen Dienste.",
  location: "Berlin",
  remote_mode: "hybrid",
  employment_type: "full_time",
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
    tenantId: null, language: "de", displayName: "Anna Beispiel",
  },
};

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/**
 * Eine Antwort in der Seitenform. Die Gesamtzahl folgt aus dem Inhalt, damit
 * ein Test nicht versehentlich eine Blätterleiste behauptet, die es nicht gibt.
 */
function seite(items: unknown[], page = 1, totalItems = items.length, pageSize = 12) {
  return {
    items,
    page,
    page_size: pageSize,
    total_items: totalItems,
    total_pages: Math.max(1, Math.ceil(totalItems / pageSize)),
    has_next: page * pageSize < totalItems,
    has_previous: page > 1,
  };
}

describe("JobsPage", () => {
  it("fragt /jobs und zeigt, was das Unternehmen geschrieben hat", async () => {
    const spion = stubFetch((url) => {
      if (url.includes("/jobs"))
        return { body: seite([STELLE]) };
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
      spion.mock.calls.some(([url]) => String(url).includes("/jobs")),
    ).toBe(true);
  });

  it("schickt leere Filter gar nicht erst mit — ein `remote=` fände nichts", async () => {
    const spion = stubFetch(() => ({ body: seite([]) }));

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
    stubFetch(() => ({ body: seite([]) }));
    renderMitStore(<JobsPage />);
    expect(await screen.findByText(/nichts gefunden/i)).toBeInTheDocument();
  });

  it("zeigt ohne Anmeldung keine Passung — und behauptet damit keine Lücke", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: seite([STELLE]) }
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
        return { body: seite([STELLE]) };
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
        return { body: seite([STELLE]) };
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
        ? { body: seite([STELLE]) }
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

  /**
   * Eine Seite ERSETZT die vorige, sie hängt nicht an.
   *
   * Das ist der Unterschied zum Vorgänger („mehr laden"), und er ist der Grund
   * für den Umbau: wer auf Seite 2 springt, will Seite 2 sehen und nicht die
   * Seiten 1 und 2 untereinander. Bliebe die erste stehen, wäre die
   * Blätterleiste eine Lüge — sie sagte „13–24 von 26" über eine Liste, die
   * vierundzwanzig Einträge zeigt.
   */
  it("springt auf die zweite Seite und ersetzt die erste", async () => {
    const zweite = {
      ...STELLE,
      id: "44444444-4444-4444-8444-444444444444",
      title: "Zweite Stelle",
    };
    stubFetch((url) => {
      if (!url.includes("/jobs")) return { status: 404 };
      return url.includes("page=2")
        ? { body: seite([zweite], 2, 24, 12) }
        : { body: seite([STELLE], 1, 24, 12) };
    });

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /Seite 2/i }));

    expect(await screen.findByText("Zweite Stelle")).toBeInTheDocument();
    expect(screen.queryByText("Backend-Entwicklung")).not.toBeInTheDocument();
  });

  /**
   * Die Karte zeigt Ort, Arbeitsform und Beschäftigung als WORTE.
   *
   * Der Test, der gefehlt hat. Er prüft nicht, dass irgendetwas dasteht,
   * sondern dass die drei Angaben AUS DEM DRAHT übersetzt ankommen — und dass
   * kein Katalogschlüssel durchschlägt. `stelle.remoteundefined` wäre für
   * `getByText` sonst genauso ein Treffer wie „Hybrid".
   */
  it("zeigt Ort, Arbeitsform und Beschäftigung übersetzt", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE]) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);

    const zeile = await screen.findByText(/Berlin/);
    expect(zeile).toHaveTextContent("Berlin");
    expect(zeile).toHaveTextContent("Hybrid");
    expect(zeile).toHaveTextContent("Vollzeit");
    // Kein roher Schlüssel — weder als „undefined" noch als Punktpfad.
    expect(zeile.textContent ?? "").not.toContain("stelle.");
    expect(zeile.textContent ?? "").not.toContain("undefined");
  });

  /** Die Leiste sagt, wo man ist — und die Zahl kommt vom Server. */
  it("nennt den Bereich und die Gesamtzahl", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE], 1, 26, 12) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("1–12 von 26")).toBeInTheDocument();
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
