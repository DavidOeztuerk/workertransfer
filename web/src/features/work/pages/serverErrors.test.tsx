import userEvent from "@testing-library/user-event";
import { cleanup, screen } from "@testing-library/react";
import type { ReactElement } from "react";
import { Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { AdvisorPage } from "./AdvisorPage";
import { ApplicationsPage } from "./ApplicationsPage";
import { AssessmentsPage } from "./AssessmentsPage";
import { DraftPage } from "./DraftPage";
import { DraftsPage } from "./DraftsPage";
import { MarketPage } from "./MarketPage";
import { TransfersPage } from "./TransfersPage";

/**
 * A GERMAN PERSON READS GERMAN when a service refuses.
 *
 * Every service answers in English — a problem document describes a shape
 * (CLAUDE.md, "Frontend") — and until 26.09.2026 these pages drew its `detail`
 * raw. Each case below feeds the page the service's own English sentence and
 * requires the catalogue's German one, and that the English appears NOWHERE on
 * the page: not instead of the German, not beside it.
 */

type Answer = { status?: number; body?: unknown };

function stubFetch(route: (url: string, init: RequestInit | undefined) => Answer) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = route(url, init);
      const status = answer.status ?? 200;
      return new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), {
        status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
}

/** The problem document as ProblemDetailsMiddleware writes it. */
const problem = (status: number, detail: string): Answer => ({
  status,
  body: { title: "Request failed", detail, correlationId: "corr-1" },
});

const PERSON = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const draw = (page: ReactElement, route = "/") =>
  renderWithStore(page, { auth: PERSON, route });

/** Nothing of the server's sentence may stand anywhere on the page. */
function noEnglish(sentence: RegExp) {
  expect(screen.queryByText(sentence)).not.toBeInTheDocument();
  expect(document.body.textContent ?? "").not.toMatch(sentence);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("a failed load reads in German", () => {
  it("on the applications page", async () => {
    stubFetch(() => problem(500, "internal error"));
    draw(<ApplicationsPage />);

    expect(await screen.findByText("Die Liste ließ sich nicht laden.")).toBeInTheDocument();
    noEnglish(/internal error/);
  });

  it("on the conversations page", async () => {
    stubFetch(() => problem(500, "internal error"));
    draw(<TransfersPage />);

    expect(await screen.findByText("Die Liste ließ sich nicht laden.")).toBeInTheDocument();
    noEnglish(/internal error/);
  });

  it("on the drafts page", async () => {
    stubFetch(() => problem(500, "internal error"));
    draw(<DraftsPage />);

    expect(
      await screen.findByText("Die Entwürfe konnten nicht geladen werden."),
    ).toBeInTheDocument();
    noEnglish(/internal error/);
  });

  it("on the draft page", async () => {
    stubFetch(() => problem(500, "internal error"));
    draw(
      <Routes>
        <Route path="/applications/drafts/:id" element={<DraftPage />} />
      </Routes>,
      "/applications/drafts/44444444-4444-4444-8444-444444444444",
    );

    expect(
      await screen.findByText("Der Entwurf konnte nicht geladen werden."),
    ).toBeInTheDocument();
    noEnglish(/internal error/);
  });

  it("on the market page, where the ledger is silent", async () => {
    stubFetch((url) =>
      url.includes("/market/me/requests")
        ? problem(503, "the consent ledger did not answer")
        : { body: STATUS },
    );
    draw(<MarketPage />);

    expect(
      await screen.findByText("Der Consent-Ledger antwortet gerade nicht."),
    ).toBeInTheDocument();
    noEnglish(/consent ledger did not answer/);
  });
});

const STATUS = {
  subject_id: PERSON.session.userId,
  availability: "listening",
  employed: true,
  note: "",
  is_approachable: false,
  updated_at: "2026-09-01T00:00:00Z",
};

describe("a refused step names the rule in German", () => {
  it("a note that is too long, on the market page", async () => {
    stubFetch((url, init) => {
      if (url.endsWith("/market/me") && init?.method === "PUT") {
        return problem(422, "The note exceeds 500 characters");
      }
      if (url.includes("/market/me/requests")) return { body: [] };
      return { body: STATUS };
    });
    draw(<MarketPage />);

    await userEvent.click(await screen.findByRole("button", { name: /^Speichern$/ }));

    expect(await screen.findByText("Die Notiz ist zu lang. Kürze sie bitte.")).toBeInTheDocument();
    noEnglish(/note exceeds/);
  });

  it("a deadline that has passed, on the work samples page", async () => {
    stubFetch((url, init) => {
      if (url.includes("/submission") && init?.method === "POST") {
        return problem(409, "the deadline for this assessment has passed");
      }
      return { body: { items: [CASE] } };
    });
    draw(<AssessmentsPage />);

    await userEvent.type(await screen.findByLabelText(/Deine Lösung/), "Mein Ergebnis");
    await userEvent.click(screen.getByRole("button", { name: /^Abgeben$/ }));

    expect(
      await screen.findByText("Die Frist für diese Arbeitsprobe ist vorbei."),
    ).toBeInTheDocument();
    noEnglish(/deadline for this assessment/);
  });

  it("an entry month in the wrong form, on the advisor page", async () => {
    stubFetch((url, init) => {
      if (url.includes("/advisor/me/mandate") && init?.method === "PUT") {
        return problem(422, "an entry date is a month, written YYYY-MM");
      }
      if (url.includes("/advisor/me/mandate")) return { body: MANDATE };
      return { body: { items: [] } };
    });
    draw(<AdvisorPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Mandat speichern/ }));

    expect(
      await screen.findByText(/Der Eintritt ist ein Monat, geschrieben JJJJ-MM/),
    ).toBeInTheDocument();
    noEnglish(/entry date is a month/);
  });

  it("a withdrawal the case has moved past, on the applications page", async () => {
    stubFetch((url, init) => {
      if (url.includes("/withdraw") && init?.method === "POST") {
        return problem(409, "A Hired application cannot become Withdrawn.");
      }
      if (url.includes("/applications/me")) return { body: [APPLICATION] };
      return { status: 404 };
    });
    draw(<ApplicationsPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Zurückziehen/ }));

    expect(
      await screen.findByText(/Diese Bewerbung lässt sich nicht mehr zurückziehen/),
    ).toBeInTheDocument();
    noEnglish(/cannot become/);
  });
});

const CASE = {
  id: "55555555-5555-4555-8555-555555555555",
  subject_id: PERSON.session.userId,
  tenant_id: "22222222-2222-4222-8222-222222222222",
  title: "Eine kleine Aufgabe",
  task: "Bau etwas Kleines.",
  hours: 2,
  due_at: "2026-10-30T00:00:00Z",
  state: "set",
  created_at: "2026-09-20T00:00:00Z",
  updated_at: "2026-09-20T00:00:00Z",
};

const MANDATE = {
  entry_month: "2026-13",
  salary_min: null,
  salary_max: null,
  workload_percent: null,
  excluded_domains: [],
};

const APPLICATION = {
  id: "66666666-6666-4666-8666-666666666666",
  job_id: "11111111-1111-4111-8111-111111111111",
  subject_id: PERSON.session.userId,
  tenant_id: "22222222-2222-4222-8222-222222222222",
  status: "submitted",
  message: "",
  shares_resume: false,
  shares_portfolio: false,
  created_at: "2026-09-20T00:00:00Z",
  updated_at: "2026-09-20T00:00:00Z",
};
