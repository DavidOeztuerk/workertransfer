import userEvent from "@testing-library/user-event";
import { cleanup, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { forgetContext } from "../lib/letterContext";
import { renderWithStore } from "../test/render";
import { JobsPage } from "./JobsPage";

/**
 * `fetch` is stubbed, not the client module.
 *
 * The old tests mocked `../jobs/client` — with that they checked the page
 * against an invention. The wire finding (`19f45b6`: camelCase never reached the
 * server) hid exactly in the layer that was skipped that way. Here path, method
 * and snake_case run through the real client.
 */
type Answer = { status?: number; body?: unknown };

function stubFetch(
  routes: (url: string, init: RequestInit | undefined) => Answer,
) {
  const spy = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = routes(url, init);
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
  vi.stubGlobal("fetch", spy);
  return spy;
}

/**
 * A job AS THE SERVICE SENDS IT — not as the UI holds it.
 *
 * <strong>`remote` and `employment` once stood here.</strong> The service
 * writes `remote_mode` and `employment_type`, though. The whole suite was green
 * while the card in the running system showed
 * `stelle.remoteundefined · stelle.employmentundefined` — because the test
 * invented a shape that never existed on the wire, and `searchJobs` cast the
 * answer to `Job` unchecked.
 *
 * The rule behind it stands in CLAUDE.md at the notification wire: writing a
 * name yourself on both sides is one string checked twice. A fixture must copy
 * the WIRE.
 *
 * And filled completely, every field: a fixture with empty fields leaves
 * exactly the branches unchecked that display something.
 */
const JOB = {
  id: "11111111-1111-4111-8111-111111111111",
  tenant_id: "22222222-2222-4222-8222-222222222222",
  title: "Backend-Entwicklung",
  description: "Wir bauen Dienste.",
  location: "Berlin",
  postal_code: "10115",
  remote_mode: "hybrid",
  employment_type: "full_time",
  skills: ["Python", "Kubernetes", "Go"],
  status: "published",
  published_at: "2026-08-01T00:00:00Z",
  updated_at: "2026-08-01T00:00:00Z",
};

const SESSION = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null, language: "de", displayName: "Anna Beispiel", givenName: "", familyName: "", occupationalField: null,
  },
};

beforeEach(() => {
  window.localStorage.clear();
});

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
  forgetContext();
});

/**
 * An answer in the page shape. The total follows from the content, so that a
 * test does not accidentally claim a pagination bar that does not exist.
 */
function page(
  items: unknown[],
  number = 1,
  totalItems = items.length,
  pageSize = 12,
  omitted?: number,
) {
  return {
    items,
    page: number,
    page_size: pageSize,
    total_items: totalItems,
    total_pages: Math.max(1, Math.ceil(totalItems / pageSize)),
    has_next: number * pageSize < totalItems,
    has_previous: number > 1,
    // Missing when searched without a radius — exactly as with the service.
    ...(omitted === undefined ? {} : { omitted }),
  };
}

/**
 * Puts a geolocation into the browser that answers as agreed.
 *
 * `jsdom` brings none at all — without this piece `"geolocation" in navigator`
 * is false, the button disabled and every probe below it green without ever
 * having checked anything.
 */
function stubGeolocation(
  answer:
    | { coords: { latitude: number; longitude: number } }
    | { code: number },
) {
  const getCurrentPosition = vi.fn(
    (
      succeeded: PositionCallback,
      failed?: PositionErrorCallback | null,
    ) => {
      if ("coords" in answer) {
        succeeded(answer as unknown as GeolocationPosition);
        return;
      }

      failed?.({
        code: answer.code,
        message: "",
        PERMISSION_DENIED: 1,
        POSITION_UNAVAILABLE: 2,
        TIMEOUT: 3,
      } as GeolocationPositionError);
    },
  );

  Object.defineProperty(navigator, "geolocation", {
    value: { getCurrentPosition },
    configurable: true,
  });

  return getCurrentPosition;
}

afterEach(() => {
  // @ts-expect-error — in jsdom the property does not exist otherwise.
  delete navigator.geolocation;
});

/**
 * A MUI select field is clicked, not `selectOption`-ed.
 *
 * It is no `<select>`: MUI draws a button with `role="combobox"` and a list with
 * `role="listbox"`. The same helper stands in `web/e2e/stack.ts` for the
 * Playwright journeys.
 */
async function chooseInField(label: RegExp, entry: string) {
  await userEvent.click(screen.getByLabelText(label));
  await userEvent.click(await screen.findByRole("option", { name: entry }));
}

describe("JobsPage", () => {
  it("asks /jobs and shows what the company wrote", async () => {
    const spy = stubFetch((url) => {
      if (url.includes("/jobs"))
        return { body: page([JOB]) };
      if (url.includes("/companies/")) {
        return {
          body: {
            tenant_id: JOB.tenant_id,
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

    renderWithStore(<JobsPage />);

    expect(await screen.findByText("Backend-Entwicklung")).toBeInTheDocument();
    expect(await screen.findByText("Acme GmbH")).toBeInTheDocument();
    expect(
      spy.mock.calls.some(([url]) => String(url).includes("/jobs")),
    ).toBe(true);
  });

  it("does not even send empty filters — a `remote=` would find nothing", async () => {
    const spy = stubFetch(() => ({ body: page([]) }));

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.type(screen.getByLabelText(/Suchbegriff/i), "Python");
    await userEvent.click(screen.getByRole("button", { name: /Suchen/i }));

    await waitFor(() => {
      const urls = spy.mock.calls.map(([url]) => String(url));
      expect(urls.some((url) => url.includes("q=Python"))).toBe(true);
    });
    expect(
      spy.mock.calls.every(([url]) => !String(url).includes("remote=")),
    ).toBe(true);
  });

  it("says on an empty result that nothing was found", async () => {
    stubFetch(() => ({ body: page([]) }));
    renderWithStore(<JobsPage />);
    expect(await screen.findByText(/nichts gefunden/i)).toBeInTheDocument();
  });

  it("shows no fit without sign-in — and so claims no gap", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: page([JOB]) }
        : { status: 404 },
    );

    renderWithStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");

    expect(
      screen.queryByText(/von 3 genannten Fähigkeiten/),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
    // The skills themselves still stand there — they belong to the advert.
    expect(screen.getByText("Python")).toBeInTheDocument();
  });

  it("counts the ticks but never names a percentage", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SESSION.session.userId,
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
        return { body: page([JOB]) };
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });

    expect(
      await screen.findByText("Du hast 2 von 3 genannten Fähigkeiten:"),
    ).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
    // Equality, not containment: "Go" stays a gap.
    expect(screen.getByText("Go").closest("li")).toHaveAttribute(
      "data-match",
      "missing",
    );
  });

  it("says “enter skills” instead of “0 of 3” when nothing is entered", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SESSION.session.userId,
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
        return { body: page([JOB]) };
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });

    expect(
      await screen.findByText(/Trage Fähigkeiten in deinem/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
  });

  /*
   * This test's predecessor was called "remembers only the UUID when applying
   * without an account, never a path" and pinned `merkeStelle`. The intent was
   * written and NEVER read: `gemerkteStelle()` had no caller outside
   * `intent.ts`, and the sign-in had expressly switched the way back off. An
   * intent that lies in a person's browser for 24 hours without anything
   * redeeming it is no half comfort but only the entry. `intent.ts` therefore
   * fell (PBI-8.3).
   *
   * This test records what holds since — and it is the reason a return would be
   * a DECISION and not an accident: whoever wants to build the way back has to
   * rewrite it here first.
   */
  it("stores nothing in the browser when applying without an account", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: page([JOB]) }
        : { status: 404 },
    );

    renderWithStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: "Bewerben" }));

    // The key the fallen `intent.ts` wrote — a real browser's storage name.
    expect(window.localStorage.getItem("wt.gemerkte-stelle")).toBeNull();
    expect(window.localStorage.length).toBe(0);
  });

  it("shows the selection boxes only after sign-in", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: page([JOB]) } : { status: 404 },
    );

    renderWithStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    expect(screen.queryByRole("checkbox", { name: /Stelle auswählen/i })).not.toBeInTheDocument();
  });

  /**
   * The progress bar was BUILT and never reached.
   *
   * `setFortschritt` was called in exactly one place — in the `finally`, with
   * `null` —, the bar read a value nobody set, and the catalogue key
   * `jobs.progress` stood in three languages without a reader (PBI-8.1). This
   * test is the reason that does not break off unnoticed again: it demands the
   * intermediate number, not only the end.
   *
   * The second job HANGS on purpose. Without it both drafts would be done at the
   * same instant, and "1 von 2" would never be on the screen — the test would be
   * green without ever having seen a progress.
   */
  it("shows during bulk writing how many drafts are done", async () => {
    const second = {
      ...JOB,
      id: "55555555-5555-4555-8555-555555555555",
      title: "Zweite Stelle",
    };
    const draft = (id: string, jobId: string) => ({
      id,
      job_id: jobId,
      subject: "",
      body: "",
      status: "open",
      shares_resume: false,
      documents: [],
      comments: [],
      error: null,
      writing_started_at: null,
    });

    let releaseSecond: () => void = () => {};
    const hangs = new Promise<void>((resolve) => {
      releaseSecond = resolve;
    });

    const answer = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": "application/json" },
      });

    vi.stubGlobal(
      "fetch",
      async (input: string | URL | Request, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.includes("/drafts/two/write")) {
          await hangs;
          return answer(draft("two", second.id));
        }
        if (url.includes("/drafts/one/write")) {
          return answer(draft("one", JOB.id));
        }
        if (url.includes("/applications/drafts")) {
          return init?.method === "POST"
            ? answer([draft("one", JOB.id), draft("two", second.id)])
            : answer([]);
        }
        if (url.includes("/applications/me")) return answer([]);
        if (url.includes("/jobs")) return answer(page([JOB, second]));
        return answer(null, 404);
      },
    );

    renderWithStore(<JobsPage />, { auth: SESSION });
    await screen.findByText("Backend-Entwicklung");

    for (const box of await screen.findAllByRole("checkbox", {
      name: /Stelle auswählen/i,
    })) {
      await userEvent.click(box);
    }
    // Opt-in: the AI writes only when the person asks for it.
    await userEvent.click(
      screen.getByRole("checkbox", { name: /Anschreiben von der KI vorschreiben lassen/i }),
    );
    await userEvent.click(
      screen.getByRole("button", { name: /Für alle bewerben/i }),
    );

    expect(await screen.findByText("1 von 2 geschrieben")).toBeInTheDocument();

    releaseSecond();
  });

  it("lets a signed-in person select several jobs", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs")) return { body: page([JOB]) };
      if (url.includes("/profiles/me")) return { status: 404 };
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });
    await screen.findByText("Backend-Entwicklung");
    const box = await screen.findByRole("checkbox", { name: /Stelle auswählen/i });
    await userEvent.click(box);
    expect(await screen.findByText(/1 Stelle ausgewählt/i)).toBeInTheDocument();
  });

  /**
   * A page REPLACES the previous one, it does not append.
   *
   * That is the difference from the predecessor ("load more"), and it is the
   * reason for the rebuild: whoever jumps to page 2 wants to see page 2 and not
   * pages 1 and 2 one below the other. If the first stayed, the pagination bar
   * would be a lie — it would say "13–24 von 26" about a list that shows
   * twenty-four entries.
   */
  it("jumps to the second page and replaces the first", async () => {
    const second = {
      ...JOB,
      id: "44444444-4444-4444-8444-444444444444",
      title: "Zweite Stelle",
    };
    stubFetch((url) => {
      if (!url.includes("/jobs")) return { status: 404 };
      return url.includes("page=2")
        ? { body: page([second], 2, 24, 12) }
        : { body: page([JOB], 1, 24, 12) };
    });

    renderWithStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /Seite 2/i }));

    expect(await screen.findByText("Zweite Stelle")).toBeInTheDocument();
    expect(screen.queryByText("Backend-Entwicklung")).not.toBeInTheDocument();
  });

  /**
   * The card shows place, work mode and employment as WORDS.
   *
   * The test that was missing. It does not check that anything stands there, but
   * that the three statements arrive translated FROM THE WIRE — and that no
   * catalogue key breaks through. `job.remoteundefined` would otherwise be as
   * much a hit for `getByText` as "Hybrid".
   */
  it("shows place, work mode and employment translated", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: page([JOB]) } : { status: 404 },
    );

    renderWithStore(<JobsPage />);

    const line = await screen.findByText(/Berlin/);
    expect(line).toHaveTextContent("Berlin");
    expect(line).toHaveTextContent("Hybrid");
    expect(line).toHaveTextContent("Vollzeit");
    // No raw key — neither as "undefined" nor as a dotted path.
    expect(line.textContent ?? "").not.toContain("job.");
    expect(line.textContent ?? "").not.toContain("undefined");
  });

  /** The bar says where one is — and the number comes from the server. */
  it("names the range and the total", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: page([JOB], 1, 26, 12) } : { status: 404 },
    );

    renderWithStore(<JobsPage />);

    expect(await screen.findByText("1–12 von 26")).toBeInTheDocument();
  });

  /**
   * <strong>The position is ROUNDED before it leaves the house.</strong>
   *
   * The most important probe of this filter, and it does not check that it
   * works but what it gives away. The address line stands in every access log;
   * a GPS value with seven decimal places would be the flat there. Two places
   * are a good kilometre — and finer could change no answer, because the adverts
   * name cities.
   */
  it("rounds the location before it goes into the address line", async () => {
    const spy = stubFetch((url) =>
      url.includes("/jobs/place")
        ? { body: { location: "Berlin" } }
        : { body: page([]) },
    );
    const located = stubGeolocation({
      coords: { latitude: 52.5170365, longitude: 13.3888599 },
    });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    expect(located).toHaveBeenCalled();

    await chooseInField(/Entfernung/i, "25 km");
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() => {
      const url = spy.mock.calls
        .map(([value]) => String(value))
        .find((value) => value.includes("radius_km"));

      expect(url).toBeDefined();
      expect(url).toContain("lat=52.52");
      expect(url).toContain("lon=13.39");
      expect(url).toContain("radius_km=25");
      // The raw value must turn up nowhere — not even truncated.
      expect(url).not.toContain("52.517");
      expect(url).not.toContain("13.388");
    });
  });

  /**
   * Without a location the distance cannot be chosen.
   *
   * A field one can operate and that then does nothing is worse than a disabled
   * one: "25 km" without a point is no question the service could answer — it
   * would silently drop it, and the list would look like a result.
   */
  it("allows the distance only once a location is there", async () => {
    stubFetch(() => ({ body: page([]) }));
    stubGeolocation({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    await waitFor(() =>
      expect(screen.getByLabelText(/Entfernung/i)).not.toHaveAttribute(
        "aria-disabled",
      ),
    );
  });

  /**
   * A refused access is called by its name.
   *
   * A shared "location not available" would leave open whether the person
   * refused it themselves — then "try again" would simply be wrong.
   */
  it("says when location access was refused", async () => {
    stubFetch(() => ({ body: page([]) }));
    stubGeolocation({ code: 1 });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    expect(await screen.findByText(/abgelehnt/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  /**
   * <strong>What the filter could not judge stands on the screen.</strong>
   *
   * A radius search can say nothing about an advert whose place the location
   * table does not know. Leaving those out silently would give a result that
   * looks complete and is not — the lie by omission from ADR-0022 §3.
   */
  it("names the adverts whose place is unknown", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: page([JOB], 1, 1, 12, 3) }
        : { status: 404 },
    );

    renderWithStore(<JobsPage />);

    expect(
      await screen.findByText(/3 Anzeigen nennen keinen Ort, den wir kennen/i),
    ).toBeInTheDocument();
  });

  /** Without a radius search there is nothing to leave out — and no hint. */
  it("reports nothing left out when searched without a radius", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: page([JOB]) } : { status: 404 },
    );

    renderWithStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");

    expect(
      screen.queryByText(/nennen keinen Ort, den wir kennen/i),
    ).not.toBeInTheDocument();
  });

  /**
   * Forgetting the location takes the distance with it.
   *
   * A "25 km" left standing without a point would look like an active filter
   * and be none — the list would silently show everything again.
   */
  it("forgets the chosen distance together with the location", async () => {
    const spy = stubFetch(() => ({ body: page([]) }));
    stubGeolocation({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    await chooseInField(/Entfernung/i, "25 km");

    await userEvent.click(
      screen.getByRole("button", { name: /Standort vergessen/i }),
    );
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() =>
      expect(
        spy.mock.calls.every(
          ([url]) => !String(url).includes("radius_km"),
        ),
      ).toBe(true),
    );
    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  /**
   * With "fully remote" the distance disappears.
   *
   * The service lets fully remote adverts through regardless of the radius —
   * the combination therefore delivers the same as "remote only" alone. A
   * control that stands there visibly and demonstrably does nothing is worse
   * than none. And the radius already chosen goes WITH it, otherwise it would
   * count as an active filter and come back unnoticed when switching back.
   */
  it("takes the distance away entirely with “fully remote”", async () => {
    const spy = stubFetch(() => ({ body: page([]) }));
    stubGeolocation({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    await chooseInField(/Entfernung/i, "25 km");

    await chooseInField(/Arbeitsform/i, "Vollständig remote");

    expect(screen.queryByLabelText(/Entfernung/i)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() => {
      const urls = spy.mock.calls.map(([url]) => String(url));
      expect(urls.some((url) => url.includes("remote=full"))).toBe(true);
    });
    expect(
      spy.mock.calls.every(([url]) => !String(url).includes("radius_km")),
    ).toBe(true);

    // And back: the field is there again — at "Egal", not at the old value.
    await chooseInField(/Arbeitsform/i, "Hybrid");
    expect(screen.getByLabelText(/Entfernung/i)).toHaveTextContent("Egal");
  });

  /**
   * <strong>The found location becomes VISIBLE.</strong>
   *
   * A radius around an invisible point is an imposition: the list changes, and
   * nobody can say from where the distance was measured. The name comes from
   * the same table as the search itself (`/jobs/place`), not from a third party.
   */
  it("puts the found location into the place field", async () => {
    stubFetch((url) =>
      url.includes("/jobs/place")
        ? { body: { location: "Berlin" } }
        : { body: page([]) },
    );
    stubGeolocation({ coords: { latitude: 52.5170365, longitude: 13.3888599 } });

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText("Ort")).toHaveValue("");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    await waitFor(() =>
      expect(screen.getByLabelText("Ort")).toHaveValue("Berlin"),
    );
  });

  /**
   * A typed place is a centre too.
   *
   * The distance used to hang on the location button alone. Whoever types
   * "Leipzig" and wants "50 km" means "around Leipzig", though — and needs
   * neither GPS nor the permission for it.
   */
  it("allows the distance without GPS too when a place stands there", async () => {
    const spy = stubFetch(() => ({ body: page([]) }));

    renderWithStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );

    await userEvent.type(screen.getByLabelText("Ort"), "Leipzig");

    expect(screen.getByLabelText(/Entfernung/i)).not.toHaveAttribute(
      "aria-disabled",
    );

    await chooseInField(/Entfernung/i, "50 km");
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    // The place goes out WITH it — the service makes it the centre because a
    // distance stands beside it. The browser has no coordinates.
    await waitFor(() => {
      const url = spy.mock.calls
        .map(([value]) => String(value))
        .find((value) => value.includes("radius_km"));

      expect(url).toBeDefined();
      expect(url).toContain("location=Leipzig");
      expect(url).toContain("radius_km=50");
      expect(url).not.toContain("lat=");
    });
  });

  /*
   * The failure reads in the person's language. The service answers in English
   * (a problem document describes a shape), and until 26.09.2026 this page
   * drew its `detail` raw — this test then expected the server's sentence.
   */
  it("shows a failed fetch as an error, not as an empty list", async () => {
    stubFetch(() => ({
      status: 500,
      body: {
        title: "Request failed",
        detail: "internal error",
        correlationId: "abc-123",
      },
    }));

    renderWithStore(<JobsPage />);

    expect((await screen.findAllByText("Die Suche ist fehlgeschlagen.")).length).toBeGreaterThan(0);
    expect(screen.queryByText(/internal error|Request failed/)).not.toBeInTheDocument();
    expect(
      screen.queryByText("Dazu wurde nichts gefunden."),
    ).not.toBeInTheDocument();
    // The correlation id is the only thread back through all services.
    expect(screen.getByText(/abc-123/)).toBeInTheDocument();
  });

  it("shows “already applied” instead of “apply”", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: page([JOB]) };
      }
      if (url.includes("/applications/me")) {
        return {
          body: [{ id: "a1", job_id: JOB.id, status: "submitted" }],
        };
      }
      if (url.includes("/applications/drafts")) return { body: [] };
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });
    await screen.findByText("Backend-Entwicklung");
    expect(screen.getByRole("button", { name: /Bereits beworben/i })).toBeDisabled();
    expect(screen.queryByRole("link", { name: /^Bewerben$/i })).toBeNull();
  });

  it("shows “open draft” when an open draft is there", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: page([JOB]) };
      }
      if (url.includes("/applications/me")) return { body: [] };
      if (url.includes("/applications/drafts")) {
        return {
          body: [
            {
              id: "d1",
              job_id: JOB.id,
              status: "review",
              subject: "",
              body: "",
              version: 1,
              error: "",
              shares_resume: true,
              documents: [],
              comments: [],
              updated_at: "2026-09-07T00:00:00Z",
            },
          ],
        };
      }
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });
    await screen.findByText("Backend-Entwicklung");
    expect(screen.getByRole("link", { name: /Entwurf öffnen/i })).toHaveAttribute(
      "href",
      "/applications/drafts/d1",
    );
    expect(screen.getByRole("checkbox", { name: /Stelle auswählen/i })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /^Bewerben$/i })).toBeNull();
  });

  it("creates an open draft on applying and writes nothing by itself", async () => {
    const spy = stubFetch((url, init) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: page([JOB]) };
      }
      if (url.includes("/applications/me")) return { body: [] };
      if (init?.method === "POST" && url.includes("/applications/drafts") && !url.includes("/write")) {
        return {
          status: 201,
          body: [
            {
              id: "d-new",
              job_id: JOB.id,
              status: "generating",
              subject: "",
              body: "",
              version: 1,
              error: "",
              shares_resume: true,
              documents: [],
              comments: [],
              updated_at: "2026-09-07T00:00:00Z",
            },
          ],
        };
      }
      if (url.includes("/write")) {
        return {
          body: {
            id: "d-new",
            job_id: JOB.id,
            status: "generating",
            subject: "",
            body: "",
            version: 1,
            error: "",
            shares_resume: true,
            documents: [],
            comments: [],
            updated_at: "2026-09-07T00:00:00Z",
          },
        };
      }
      if (url.includes("/applications/drafts")) return { body: [] };
      return { status: 404 };
    });

    renderWithStore(<JobsPage />, { auth: SESSION });
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /^Bewerben$/i }));

    await waitFor(() => {
      const posts = spy.mock.calls.filter(
        ([, init]) => (init as RequestInit | undefined)?.method === "POST",
      );
      expect(
        posts.some(([url]) => String(url).includes("/applications/drafts") && !String(url).includes("/write")),
      ).toBe(true);
    });
    // A writing costs the person's AI credit — applying alone spends nothing.
    const writes = spy.mock.calls.filter(([url]) => String(url).includes("/write"));
    expect(writes).toHaveLength(0);
  });
});
