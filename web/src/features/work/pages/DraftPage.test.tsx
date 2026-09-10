import { screen } from "@testing-library/react";
import { ThemeProvider } from "@mui/material/styles";
import { configureStore } from "@reduxjs/toolkit";
import { render } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import preferences from "../../../core/store/preferencesSlice";
import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import auth from "../../auth/store/authSlice";
import { vergissKontext } from "../lib/kontext";
import { DraftPage } from "./DraftPage";

const ID = "01a078bd-ec65-7b57-b444-d25c53edbcda";

const ENTWURF = {
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

type Antwort = { status?: number; body?: unknown };

function stubFetch(routen: (url: string) => Antwort) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = routen(url);
      const status = answer.status ?? 200;
      return new Response(JSON.stringify(answer.body ?? null), {
        status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vergissKontext();
});

function zeichne() {
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
  it("lässt auf Fassung 1 ohne Brief nicht bearbeiten und bietet Generieren", async () => {
    stubFetch((url) => {
      if (url.includes(`/applications/drafts/${ID}`)) return { body: ENTWURF };
      if (url.includes("/resume") || url.includes("/documents")) return { body: [] };
      return { status: 404 };
    });

    zeichne();

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

  it("hängt den Lebenslauf immer an und bietet Anpassen statt Vorlagen", async () => {
    stubFetch((url) => {
      if (url.includes(`/applications/drafts/${ID}`)) {
        return {
          body: {
            ...ENTWURF,
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

    zeichne();

    expect(await screen.findByText(/Der Lebenslauf geht immer mit/i)).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: /Lebenslauf bearbeiten/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /schlicht/i })).not.toBeInTheDocument();
  });

  it("holt während des Schreibens weder Lebenslauf noch Firmenprofil", async () => {
    const gefragt: string[] = [];
    stubFetch((url) => {
      gefragt.push(url);
      if (url.includes(`/applications/drafts/${ID}`)) {
        return {
          body: {
            ...ENTWURF,
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
            id: ENTWURF.job_id,
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

    zeichne();

    expect(await screen.findByText("Wird geschrieben")).toBeInTheDocument();
    expect(gefragt.some((url) => url.includes("/resumes/me"))).toBe(false);
    expect(gefragt.some((url) => url.includes("/companies/") && url.includes("/profile"))).toBe(
      false,
    );
  });
});
