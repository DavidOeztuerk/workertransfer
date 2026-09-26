import { cleanup, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import { renderWithStore } from "../../work/test/render";
import { CompanyAdvisorPage } from "./CompanyAdvisorPage";

/**
 * The one promise of this page, as a test.
 *
 * <strong>What is not released at a stage does not exist for the other
 * side</strong> (ADR-0037). The server leaves the field out of the JSON
 * entirely; this page must make no hint of it — no "not released yet", no
 * lock, no grey line. Such a hint would reveal exactly what the stage holds
 * back: that there is something.
 *
 * <strong>`fetch` is stubbed, not the client module.</strong> That way path,
 * method and snake_case run through the real client — a field the page reads
 * under another name would otherwise never arrive, and the test would stay
 * green.
 */
function stubFetch(body: unknown) {
  const spy = vi.fn(
    async () =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
  );

  vi.stubGlobal("fetch", spy);
  return spy;
}

const COMPANY = "11111111-1111-1111-1111-111111111111";
const ANNA = "33333333-3333-3333-3333-333333333333";

/** A session acting for a company. */
const asCompany = {
  status: "authenticated" as const,
  session: {
    userId: "22222222-2222-2222-2222-222222222222",
    email: "recruiter@example.test",
    displayName: "Recruiter",
    tenantId: COMPANY,
  } as never,
};

afterEach(() => {
  vi.unstubAllGlobals();
  cleanup();
});

it("shows neither salary nor name at stage 1 — and no hint of them", async () => {
  // EXACTLY THE SERVER'S SHAPE: the fields of the higher stages are MISSING,
  // they are not `null`. A `null` here would be an invented shape, and the test
  // would check something that never exists on the wire.
  stubFetch({
    items: [
      {
        id: "44444444-4444-4444-4444-444444444444",
        subject_id: ANNA,
        state: "talking",
        stage: 1,
        note: "We are looking for someone for distributed systems.",
        opened_at: "2026-09-11T10:00:00Z",
        updated_at: "2026-09-11T10:00:00Z",
        entry_month: "2026-11",
        workload_percent: 80,
      },
    ],
  });

  renderWithStore(<CompanyAdvisorPage />, { auth: asCompany });

  await waitFor(() => expect(screen.getByText(/2026-11/)).toBeInTheDocument());

  // What is released stands there.
  expect(screen.getByText(/80/)).toBeInTheDocument();

  // And what is not released is missing ENTIRELY — not as an empty field, not
  // as a hint, not as a placeholder.
  expect(screen.queryByText(/€/)).not.toBeInTheDocument();
  expect(screen.queryByText(/Anna/)).not.toBeInTheDocument();
  expect(screen.queryByText(/gesperrt|freigegeben werden|noch nicht/i)).not.toBeInTheDocument();

  // Without a legal name the id stands there and no invented placeholder: an
  // "anonymous" would claim the person hid themselves.
  expect(screen.getByText(ANNA)).toBeInTheDocument();
});

it("shows name, address and range at stage 3", async () => {
  stubFetch({
    items: [
      {
        id: "44444444-4444-4444-4444-444444444444",
        subject_id: ANNA,
        state: "agreed",
        stage: 3,
        note: "",
        opened_at: "2026-09-11T10:00:00Z",
        updated_at: "2026-09-11T10:00:00Z",
        entry_month: "2026-11",
        workload_percent: 80,
        salary_min: 4000,
        salary_max: 5200,
        name: "Anna Example",
        email: "anna@example.test",
      },
    ],
  });

  renderWithStore(<CompanyAdvisorPage />, { auth: asCompany });

  await waitFor(() => expect(screen.getByText("Anna Example")).toBeInTheDocument());

  expect(screen.getByText("anna@example.test")).toBeInTheDocument();
  expect(screen.getByText(/4000–5200/)).toBeInTheDocument();

  // After the agreement handing over is allowed — and only then.
  expect(screen.getByRole("button", { name: /Transfer/ })).toBeInTheDocument();
});

it("treats an empty list as an answer, not an error", async () => {
  stubFetch({ items: [] });

  renderWithStore(<CompanyAdvisorPage />, { auth: asCompany });

  await waitFor(() =>
    expect(screen.getByText("Es läuft gerade kein Gespräch.")).toBeInTheDocument(),
  );
});

it("has nothing to show without a company", async () => {
  const spy = stubFetch({ items: [] });

  renderWithStore(<CompanyAdvisorPage />, {
    auth: {
      status: "authenticated",
      session: {
        userId: ANNA,
        email: "anna@example.test",
        displayName: "Anna",
        tenantId: null,
      } as never,
    },
  });

  expect(
    screen.getByText(/Gespräche führen nur Unternehmen/),
  ).toBeInTheDocument();

  // And nothing is asked at all: a request that is sure to get 403 is an error
  // message nobody needs.
  expect(spy).not.toHaveBeenCalled();
});
