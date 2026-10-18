import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { CompanyJobEditPage } from "./CompanyJobNewPage";

/**
 * Changing an advert — and a fetched one keeps its text (ADR-0052): the
 * company adds skills, salary and postal code, the source owns the rest.
 */

type Call = { url: string; method: string; body: unknown };

const TENANT = "22222222-2222-4222-8222-222222222222";
const JOB_ID = "11111111-1111-4111-8111-111111111111";

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@firma.de",
    tenantId: TENANT,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const JOB = {
  id: JOB_ID,
  tenant_id: TENANT,
  title: "Schweißer (m/w/d)",
  description: "Wir schweißen Geländer.",
  location: "Leipzig",
  postal_code: "",
  remote_mode: "none",
  employment_type: "full_time",
  skills: [],
  status: "published",
  published_at: "2026-10-18T09:00:00Z",
  updated_at: "2026-10-18T09:00:00Z",
};

function stubFetch(jobs: unknown[], save: () => { status: number; body: unknown }): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const method = init?.method ?? "GET";
      calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : null });
      const answer = method === "PUT" ? save() : { status: 200, body: url.includes("/companies/me/jobs") ? jobs : null };
      return new Response(JSON.stringify(answer.body), {
        status: answer.status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return calls;
}

const draw = () =>
  renderWithStore(
    <Routes>
      <Route path="/company/jobs/:id/edit" element={<CompanyJobEditPage />} />
      <Route path="/company/jobs" element={<p>Liste</p>} />
    </Routes>,
    { auth: FOR_COMPANY, route: `/company/jobs/${JOB_ID}/edit` },
  );

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("CompanyJobEditPage", () => {
  it("a fetched advert keeps its text and takes skills", async () => {
    const calls = stubFetch([{ ...JOB, mirrored: true, source_url: "https://boards.greenhouse.io/acme/jobs/1" }], () => ({
      status: 200,
      body: JOB,
    }));
    draw();

    expect(await screen.findByText(/kommen aus eurer Quelle/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Titel/)).toBeDisabled();
    expect(screen.getByLabelText(/Beschreibung/)).toBeDisabled();
    expect(screen.queryByText(/Vorschlag holen/)).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Gesuchte Fähigkeiten/), "MIG/MAG, WIG");
    await userEvent.click(screen.getByRole("button", { name: "Änderungen speichern" }));

    expect(await screen.findByText("Liste")).toBeInTheDocument();
    const put = calls.find((call) => call.method === "PUT");
    expect(put?.url).toContain(`/jobs/${JOB_ID}`);
    expect(put?.body).toMatchObject({
      title: "Schweißer (m/w/d)",
      description: "Wir schweißen Geländer.",
      remote_mode: "none",
      employment_type: "full_time",
      skills: ["MIG/MAG", "WIG"],
    });
  });

  it("an own advert is edited in full", async () => {
    stubFetch([JOB], () => ({ status: 200, body: JOB }));
    draw();

    expect(await screen.findByLabelText(/Titel/)).toBeEnabled();
    expect(screen.queryByText(/kommen aus eurer Quelle/)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Titel/)).toHaveValue("Schweißer (m/w/d)");
  });

  it("the source's refusal reads in German", async () => {
    stubFetch([{ ...JOB, mirrored: true }], () => ({
      status: 409,
      body: { title: "Request failed", detail: "This advert mirrors a source — change its text there.", correlationId: "c" },
    }));
    draw();

    await userEvent.click(await screen.findByRole("button", { name: "Änderungen speichern" }));

    expect(await screen.findByText(/Der Text dieser Stelle gehört eurer Quelle/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/mirrors a source/);
  });

  it("an advert of another company is not there", async () => {
    stubFetch([], () => ({ status: 200, body: null }));
    draw();

    expect(await screen.findByText(/Diese Stelle gibt es nicht/)).toBeInTheDocument();
  });
});
