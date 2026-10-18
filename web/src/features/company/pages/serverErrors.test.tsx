import userEvent from "@testing-library/user-event";
import { cleanup, fireEvent, screen } from "@testing-library/react";
import type { ReactElement } from "react";
import { Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../../work/test/render";
import { CareerPage } from "./CareerPage";
import { CompanyAdvisorPage } from "./CompanyAdvisorPage";
import { CompanyApplicationPage } from "./CompanyApplicationPage";
import { CompanyApplicationsPage } from "./CompanyApplicationsPage";
import { CompanyAssessmentsPage } from "./CompanyAssessmentsPage";
import { CompanyJobNewPage } from "./CompanyJobNewPage";
import { CompanyJobsPage } from "./CompanyJobsPage";
import { CompanyProfilePage } from "./CompanyProfilePage";
import { CompanyTeamInvitePage } from "./CompanyTeamInvitePage";
import { CompanyTeamPage } from "./CompanyTeamPage";
import { CompanyTransfersPage } from "./CompanyTransfersPage";
import { ScoutPage } from "./ScoutPage";

/**
 * A GERMAN PERSON AT A COMPANY READS GERMAN when a service refuses.
 *
 * Every service answers in English, and until 26.09.2026 these pages drew the
 * problem document's `detail` raw. Each case feeds the service's own sentence
 * and requires the catalogue's German one — and that the English stands
 * nowhere on the page.
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

const problem = (status: number, detail: string): Answer => ({
  status,
  body: { title: "Request failed", detail, correlationId: "corr-1" },
});

const TENANT = "22222222-2222-4222-8222-222222222222";
const USER = "33333333-3333-4333-8333-333333333333";

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: USER,
    email: "a@firma.de",
    tenantId: TENANT,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const draw = (page: ReactElement, route = "/") =>
  renderWithStore(page, { auth: FOR_COMPANY, route });

function noEnglish(sentence: RegExp) {
  expect(document.body.textContent ?? "").not.toMatch(sentence);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("a failed load reads in German", () => {
  const cases: [string, () => ReactElement, string, string?][] = [
    ["the job list", () => <CompanyJobsPage />, "Die Liste ließ sich nicht laden."],
    ["the team", () => <CompanyTeamPage />, "Die Mannschaft ließ sich nicht laden."],
    ["the transfers", () => <CompanyTransfersPage />, "Die Liste ließ sich nicht laden."],
    ["the applications", () => <CompanyApplicationsPage />, "Die Liste ließ sich nicht laden."],
    ["the scout", () => <ScoutPage />, "Die Suche ist fehlgeschlagen."],
    [
      "one application",
      () => (
        <Routes>
          <Route path="/company/applications/:id" element={<CompanyApplicationPage />} />
        </Routes>
      ),
      "Die Unternehmensbewerbung konnte nicht geladen werden.",
      "/company/applications/66666666-6666-4666-8666-666666666666",
    ],
  ];

  it.each(cases)("on %s", async (_name, page, german, route) => {
    stubFetch(() => problem(500, "internal error"));
    draw(page(), route);

    expect((await screen.findAllByText(german)).length).toBeGreaterThan(0);
    noEnglish(/internal error/);
  });

  it("on a careers page whose service is silent", async () => {
    stubFetch(() => problem(503, "the service did not answer"));
    renderWithStore(
      <Routes>
        <Route path="/careers/:slug" element={<CareerPage />} />
      </Routes>,
      { route: "/careers/acme" },
    );

    expect(await screen.findByText(/Diese Seite ist gerade nicht abrufbar\./)).toBeInTheDocument();
    noEnglish(/did not answer/);
  });
});

describe("a refused step reads in German", () => {
  /*
   * The case jobs-journey found on the running stack: "No draft provider is
   * set up." in the middle of the German advert form. The sentence names the
   * CAUSE, because a configuration is something somebody can change.
   */
  it("the drafting help without a provider names the cause", async () => {
    stubFetch(() => problem(503, "No draft provider is set up."));
    draw(<CompanyJobNewPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Vorschlag holen/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /kein Entwurfsanbieter eingerichtet/i,
    );
    noEnglish(/No draft provider/);
  });

  it("the drafting help with a silent provider says it is unavailable", async () => {
    stubFetch(() => problem(503, "The draft provider does not answer (timeout)."));
    draw(<CompanyJobNewPage />);

    await userEvent.click(await screen.findByRole("button", { name: /Vorschlag holen/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die Formulierungshilfe ist gerade nicht verfügbar.",
    );
    noEnglish(/draft provider/);
  });

  it("publishing without the admin role says who may", async () => {
    stubFetch((url, init) => {
      if (url.includes("/publish") && init?.method === "POST") return problem(403, "not permitted");
      if (url.includes("/jobs/sources")) return { body: [] };
      return { body: [JOB] };
    });
    draw(<CompanyJobsPage />);

    await userEvent.click(await screen.findByRole("button", { name: "Veröffentlichen" }));

    expect(
      await screen.findByText("Das darf nur, wer Administrator dieses Unternehmens ist."),
    ).toBeInTheDocument();
    noEnglish(/not permitted/);
  });

  it("a website without a host, on the company profile", async () => {
    stubFetch((url, init) => {
      if (url.includes("/companies/me/profile") && init?.method === "PUT") {
        return problem(422, "The link is missing a host");
      }
      return { body: null };
    });
    draw(<CompanyProfilePage />);

    await userEvent.type(await screen.findByLabelText(/Anzeigename/), "Acme");
    await userEvent.click(screen.getByRole("button", { name: "Speichern" }));

    expect(
      await screen.findByText(/Die Website muss eine vollständige Adresse/),
    ).toBeInTheDocument();
    noEnglish(/missing a host/);
  });

  it("an address the server does not take, when inviting", async () => {
    stubFetch((url, init) => {
      if (url.includes("/invitations") && init?.method === "POST") {
        return problem(422, "invalid: email");
      }
      return { body: [{ user_id: USER, email: "a@firma.de", display_name: "Anna", role: "admin" }] };
    });
    draw(<CompanyTeamInvitePage />);

    await userEvent.type(await screen.findByLabelText(/E-Mail-Adresse/), "b@firma.de");
    await userEvent.click(screen.getByRole("button", { name: "Einladen" }));

    expect(
      await screen.findByText(/Das sieht nicht nach einer E-Mail-Adresse aus\./),
    ).toBeInTheDocument();
    noEnglish(/invalid: email/);
  });

  it("a deadline too close, when setting a task", async () => {
    stubFetch((url, init) => {
      if (url.endsWith("/assessments") && init?.method === "POST") {
        return problem(422, "a deadline lies at least 48 hours ahead");
      }
      return { body: { items: [] } };
    });
    draw(<CompanyAssessmentsPage />);

    await userEvent.type(await screen.findByLabelText(/^Wem/), USER);
    await userEvent.type(screen.getByLabelText(/^Überschrift/), "Probe");
    await userEvent.type(screen.getByLabelText(/^Die Aufgabe/), "Bau etwas.");
    fireEvent.change(screen.getByLabelText(/^Bis wann/), { target: { value: "2026-09-27" } });
    await userEvent.click(screen.getByRole("button", { name: "Aufgabe stellen" }));

    expect(await screen.findByText(/Die Frist ist zu knapp/)).toBeInTheDocument();
    noEnglish(/deadline lies at least/);
  });

  it("a hand-over the transfer market refuses, on the conversations page", async () => {
    stubFetch((url, init) => {
      if (url.includes("/hand-over") && init?.method === "POST") {
        return problem(409, "The market status is not released to this company");
      }
      return { body: { items: [CONVERSATION] } };
    });
    draw(<CompanyAdvisorPage />);

    await userEvent.click(await screen.findByRole("button", { name: /In einen Transfer überführen/ }));

    expect(
      await screen.findByText("Dieser Schritt ist von hier aus nicht möglich."),
    ).toBeInTheDocument();
    noEnglish(/not released/);
  });
});

const JOB = {
  id: "11111111-1111-4111-8111-111111111111",
  tenant_id: TENANT,
  title: "Backend-Entwicklung",
  description: "Wir bauen Dienste.",
  location: "Berlin",
  postal_code: "10115",
  remote_mode: "hybrid",
  employment_type: "full_time",
  skills: [],
  status: "draft",
  published_at: null,
  updated_at: "2026-09-01T00:00:00Z",
};

const CONVERSATION = {
  id: "77777777-7777-4777-8777-777777777777",
  subject_id: USER,
  state: "agreed",
  stage: 1,
  note: "",
  opened_at: "2026-09-20T00:00:00Z",
  updated_at: "2026-09-20T00:00:00Z",
};
