import userEvent from "@testing-library/user-event";
import { cleanup, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { ReleaseBlock } from "./ReleaseBlock";
import { EmployerRouteCard, EmployerRouteOffer } from "./EmployerRoute";
import { EmployerInquiries } from "../../company/components/EmployerInquiries";

const TRANSFER = "aaaaaaaa-0000-4000-8000-000000000001";
const NEGOTIATION = "bbbbbbbb-0000-4000-8000-000000000002";
const EMPLOYER = "cccccccc-0000-4000-8000-000000000003";
const HIRING = "dddddddd-0000-4000-8000-000000000004";

type Answer = { status?: number; body?: unknown };
type Call = { url: string; method: string; body: unknown };

function stubFetch(routes: (url: string, method: string) => Answer | undefined): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const method = init?.method ?? "GET";
      calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : null });
      if (url.includes("/companies/") && url.endsWith("/profile")) {
        const id = url.includes(EMPLOYER) ? EMPLOYER : HIRING;
        return json(200, { tenant_id: id, display_name: id === EMPLOYER ? "Stahlbau Nord" : "Werft Süd" });
      }
      const answer = routes(url, method) ?? { status: 404, body: { title: "Request failed", detail: "Not Found" } };
      return answer.status === 204 ? new Response(null, { status: 204 }) : json(answer.status ?? 200, answer.body ?? null);
    }),
  );
  return calls;
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const release = (overrides: Record<string, unknown>) => ({
  id: NEGOTIATION,
  transfer_id: TRANSFER,
  hiring_id: HIRING,
  employer_id: EMPLOYER,
  side: "hiring",
  status: "asked",
  release_month: "2026-12",
  fee_cents: 500_000,
  terms_by: "hiring",
  regular_from: "2027-04-01",
  updated_at: "2026-10-16T08:00:00Z",
  ...overrides,
});

describe("the release negotiation in a transfer card", () => {
  it("draws nothing when the server says there is no route", async () => {
    const calls = stubFetch(() => undefined);
    const { container } = renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    await waitFor(() => expect(calls.some((call) => call.url.endsWith("/release"))).toBe(true));
    // Not "not possible" — the difference is what the server refuses to tell.
    expect(container).toBeEmptyDOMElement();
  });

  it("lets the hiring company ask, with no figure filled in, and sends cents", async () => {
    const calls = stubFetch((url, method) =>
      method === "POST" && url.endsWith(`/transfers/${TRANSFER}/release`)
        ? { status: 201, body: release({}) }
        : { body: release({ id: null, status: "possible", release_month: null, fee_cents: null, terms_by: null }) },
    );
    renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    expect(await screen.findByText(/Stahlbau Nord lässt Anfragen zu/)).toBeInTheDocument();
    const fee = screen.getByLabelText("Ablöse in Euro");
    expect(fee).toHaveValue("");
    expect(screen.getByText("Diese Plattform schlägt keinen Preis vor.")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Freigabe zum/), "2026-12");
    await userEvent.type(fee, "5000,50");
    await userEvent.click(screen.getByRole("button", { name: "Beim Arbeitgeber anfragen" }));

    await waitFor(() => expect(calls.some((call) => call.method === "POST")).toBe(true));
    expect(calls.find((call) => call.method === "POST")?.body).toEqual({ release_month: "2026-12", fee_cents: 500_050 });
  });

  it("offers the hiring company to accept only terms the employer set", async () => {
    stubFetch(() => ({ body: release({ terms_by: "current" }) }));
    renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    expect(await screen.findByRole("button", { name: "Bedingungen annehmen" })).toBeInTheDocument();
    cleanup();

    stubFetch(() => ({ body: release({ terms_by: "hiring" }) }));
    renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    expect(await screen.findByRole("button", { name: "Anfrage zurückziehen" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Bedingungen annehmen" })).not.toBeInTheDocument();
  });

  it("asks the person last, with a no beside the yes", async () => {
    const calls = stubFetch((_url, method) =>
      method === "POST" ? { body: release({ side: "person", status: "settled" }) } : { body: release({ side: "person", status: "agreed" }) },
    );
    renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    expect(await screen.findByText(/Erst mit deiner Zustimmung gilt es/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ablehnen" })).toBeInTheDocument();
    expect(screen.getByText("5.000,00 €")).toBeInTheDocument();
    expect(screen.getByText("2027-04-01")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Zustimmen" }));

    await waitFor(() =>
      expect(calls.some((call) => call.method === "POST" && call.url.endsWith(`/transfers/releases/${NEGOTIATION}/agree`))).toBe(true),
    );
  });

  it("says when she stated no notice period instead of leaving a blank", async () => {
    stubFetch(() => ({ body: release({ side: "person", regular_from: null, fee_cents: null }) }));
    renderWithStore(<ReleaseBlock transferId={TRANSFER} />);

    expect(await screen.findByText("Die Person hat keine Kündigungsfrist angegeben.")).toBeInTheDocument();
    expect(screen.getByText("ohne Ablöse")).toBeInTheDocument();
  });
});

describe("the employer's inquiries", () => {
  it("shows the switch and an inquiry with the person's name, and lets the employer accept or refuse", async () => {
    const calls = stubFetch((url, method) => {
      if (url.endsWith("/transfers/employer/setting")) return { body: { accepts_inquiries: method === "PUT" } };
      if (url.endsWith("/transfers/employer/inquiries"))
        return { body: [release({ side: "current", person_name: "Anna Schmidt" })] };
      return { body: release({ side: "current", status: "agreed" }) };
    });
    renderWithStore(<EmployerInquiries />);

    const toggle = await screen.findByRole("switch", { name: /Anfragen anderer Firmen zu unserem Personal zulassen/ });
    expect(toggle).not.toBeChecked();
    expect(await screen.findByText("Zu Anna Schmidt")).toBeInTheDocument();
    expect(screen.getByText(/Von Werft Süd/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ablehnen" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Bedingungen annehmen" }));
    await waitFor(() => expect(calls.some((call) => call.url.endsWith(`/releases/${NEGOTIATION}/accept`))).toBe(true));

    await userEvent.click(toggle);
    await waitFor(() =>
      expect(calls.find((call) => call.method === "PUT")?.body).toEqual({ accepts_inquiries: true }),
    );
  });

  it("names nobody when nobody asked", async () => {
    stubFetch((url) =>
      url.endsWith("/setting") ? { body: { accepts_inquiries: true } } : { body: [] },
    );
    renderWithStore(<EmployerInquiries />);

    expect(await screen.findByText("Keine Anfrage.")).toBeInTheDocument();
  });
});

describe("the person's route to her employer", () => {
  it("says what follows before the button, and naming sends the company", async () => {
    const calls = stubFetch((_url, method) =>
      method === "PUT"
        ? { body: { employer_id: EMPLOYER, open: true, opened_at: "2026-10-16T08:00:00Z" } }
        : { body: { employer_id: null, open: false, opened_at: null } },
    );
    renderWithStore(<EmployerRouteOffer companyId={EMPLOYER} companyName="Stahlbau Nord" />);

    // Folded: an employee who came for the jobs is not invited to anything.
    await userEvent.click(await screen.findByRole("button", { name: /Weg über deinen Arbeitgeber/ }));
    expect(screen.getByText(/Dein Arbeitgeber erfährt nichts, solange niemand fragt/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Als meinen Arbeitgeber nennen und Weg öffnen" }));

    await waitFor(() =>
      expect(calls.find((call) => call.method === "PUT")?.body).toEqual({ employer_id: EMPLOYER }),
    );
  });

  it("warns that naming another employer ends the first route", async () => {
    stubFetch(() => ({ body: { employer_id: HIRING, open: true, opened_at: "2026-10-01T08:00:00Z" } }));
    renderWithStore(<EmployerRouteOffer companyId={EMPLOYER} companyName="Stahlbau Nord" />);

    await userEvent.click(await screen.findByRole("button", { name: /Weg über deinen Arbeitgeber/ }));
    expect(screen.getByText(/Damit endet der Weg zu dem Arbeitgeber/)).toBeInTheDocument();
  });

  it("shows the open route on the market page and closes it", async () => {
    const calls = stubFetch((_url, method) =>
      method === "DELETE" ? { status: 204 } : { body: { employer_id: EMPLOYER, open: true, opened_at: "2026-10-16T08:00:00Z" } },
    );
    renderWithStore(<EmployerRouteCard />);

    expect(await screen.findByText(/Offen zu Stahlbau Nord/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Weg schließen" }));

    await waitFor(() => expect(calls.some((call) => call.method === "DELETE")).toBe(true));
  });

  it("reads a revocation in the ledger as a closed route", async () => {
    stubFetch(() => ({ body: { employer_id: EMPLOYER, open: false, opened_at: "2026-10-16T08:00:00Z" } }));
    renderWithStore(<EmployerRouteCard />);

    expect(await screen.findByText(/geschlossen, weil du die Zustimmung zurückgenommen hast/)).toBeInTheDocument();
  });
});
