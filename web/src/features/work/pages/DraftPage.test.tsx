import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material/styles";
import { configureStore } from "@reduxjs/toolkit";
import { render } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import preferences from "../../../core/store/preferencesSlice";
import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import auth from "../../auth/store/authSlice";
import { forgetContext } from "../lib/letterContext";
import { DraftPage } from "./DraftPage";

const ID = "01a078bd-ec65-7b57-b444-d25c53edbcda";

const DRAFT = {
  id: ID,
  job_id: "11111111-1111-4111-8111-111111111111",
  subject: "",
  body: "",
  status: "failed",
  version: 1,
  error: "Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).",
  shares_resume: false,
  documents: [],
  comments: [],
  updated_at: "2026-09-07T00:00:00Z",
};

type Answer = { status?: number; body?: unknown };

function stubFetch(routes: (url: string, init: RequestInit | undefined) => Answer) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = routes(url, init);
      const status = answer.status ?? 200;
      return new Response(JSON.stringify(answer.body ?? null), {
        status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
}

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

function draw() {
  const store = configureStore({
    reducer: { auth, preferences },
    preloadedState: {
      auth: {
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
        memberships: [],
        error: null,
        pending: false,
      },
    },
  });

  return render(
    <Provider store={store}>
      <I18nextProvider i18n={i18n}>
        <ThemeProvider theme={buildTheme("light")}>
          <MemoryRouter initialEntries={[`/applications/drafts/${ID}`]}>
            <Routes>
              <Route path="/applications/drafts/:id" element={<DraftPage />} />
            </Routes>
          </MemoryRouter>
        </ThemeProvider>
      </I18nextProvider>
    </Provider>,
  );
}

describe("DraftPage", () => {
  it("offers no editing on version 1 without a letter and offers Generieren", async () => {
    stubFetch((url) => {
      if (url.includes(`/applications/drafts/${ID}`)) return { body: DRAFT };
      if (url.includes("/resume") || url.includes("/documents")) return { body: [] };
      return { status: 404 };
    });

    draw();

    expect(await screen.findByText("Noch kein Anschreiben.")).toBeInTheDocument();
    expect(screen.getByText(/zu lange nichts geliefert/i)).toBeInTheDocument();
    expect(screen.queryByText(/nicht eingerichtet/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Generieren/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Selbst schreiben/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Schreiben$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Bearbeiten/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Kommentar hinzufügen/i)).not.toBeInTheDocument();
    expect(screen.getByText(/Kommentieren geht, sobald ein Text da ist/i)).toBeInTheDocument();
  });

  it("always attaches the CV and offers adjusting instead of templates", async () => {
    stubFetch((url) => {
      if (url.includes(`/applications/drafts/${ID}`)) {
        return {
          body: {
            ...DRAFT,
            status: "review",
            error: "",
            subject: "Bewerbung",
            body: "Sehr geehrte Damen und Herren",
            shares_resume: true,
          },
        };
      }
      if (url.includes("/documents")) return { body: [] };
      if (url.includes("/resumes/me")) {
        return { body: { template: "schlicht", positions: [], education: [] } };
      }
      if (url.includes("/account/address")) {
        return {
          body: {
            line1: "",
            line2: "",
            postal_code: "",
            city: "",
            country: "DE",
            phone: "",
          },
        };
      }
      return { status: 404 };
    });

    draw();

    expect(await screen.findByText(/Der Lebenslauf geht immer mit/i)).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: /Lebenslauf bearbeiten/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /schlicht/i })).not.toBeInTheDocument();
  });

  it("fetches neither CV nor company profile while writing", async () => {
    const asked: string[] = [];
    stubFetch((url) => {
      asked.push(url);
      if (url.includes(`/applications/drafts/${ID}`)) {
        return {
          body: {
            ...DRAFT,
            status: "generating",
            error: "",
            writing_started_at: "2026-09-07T09:00:00Z",
          },
        };
      }
      if (url.includes("/account/address")) {
        return {
          body: {
            line1: "Weg 1",
            line2: "",
            postal_code: "80331",
            city: "München",
            country: "DE",
            phone: "",
          },
        };
      }
      if (url.includes("/jobs/")) {
        return {
          body: {
            id: DRAFT.job_id,
            tenant_id: "44444444-4444-4444-8444-444444444444",
            title: "Stelle",
            description: "",
            location: "München",
            postal_code: "80331",
            remote_mode: "none",
            employment_type: "full_time",
            skills: [],
            status: "published",
            published_at: null,
            updated_at: "2026-09-07T00:00:00Z",
          },
        };
      }
      return { status: 404 };
    });

    draw();

    expect(await screen.findByText("Wird geschrieben")).toBeInTheDocument();
    expect(asked.some((url) => url.includes("/resumes/me"))).toBe(false);
    expect(asked.some((url) => url.includes("/companies/") && url.includes("/profile"))).toBe(
      false,
    );
  });

  /*
   * A GERMAN PERSON READS GERMAN when a step fails.
   *
   * applications-service answers in English since 62e819e, and until
   * 26.09.2026 this page put the problem document's `detail` straight into the
   * alert: "This job is no longer advertised." in the middle of a German page.
   * The page now reads the status and the sentence and shows the catalogue text
   * (`lib/draftErrors.ts`). The server's sentence must not appear at all — not
   * even beside the German one.
   */
  it("shows a German sentence and not the server's English when sending fails", async () => {
    stubFetch((url, init) => {
      if (url.includes(`/applications/drafts/${ID}/send`) && init?.method === "POST") {
        return {
          status: 409,
          body: {
            title: "Request failed",
            detail: "This job is no longer advertised.",
            correlationId: "abc-123",
          },
        };
      }
      if (url.includes(`/applications/drafts/${ID}`)) {
        return {
          body: {
            ...DRAFT,
            status: "approved",
            error: "",
            subject: "Bewerbung",
            body: "Sehr geehrte Damen und Herren",
            shares_resume: true,
          },
        };
      }
      if (url.includes("/documents")) return { body: [] };
      return { status: 404 };
    });

    draw();

    await userEvent.click(await screen.findByRole("button", { name: /^Senden$/ }));

    expect(await screen.findByText("Diese Stelle ist nicht mehr offen.")).toBeInTheDocument();
    expect(screen.queryByText(/no longer advertised/i)).not.toBeInTheDocument();
  });
});
