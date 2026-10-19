import { cleanup, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { JobSources } from "./JobSources";

/**
 * The sources card (ADR-0052): the code stands before anything is fetched, a
 * fetch reports what it did, and the server's sentences read in German.
 */

type Answer = { status?: number; body?: unknown };
type Call = { url: string; method: string; body: unknown };

function stubFetch(route: (url: string, method: string) => Answer): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const method = init?.method ?? "GET";
      calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : null });
      const answer = route(url, method);
      const status = answer.status ?? 200;
      return new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), {
        status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return calls;
}

const problem = (status: number, detail: string): Answer => ({
  status,
  body: { title: "Request failed", detail, correlationId: "corr-1" },
});

const UNPROVEN = {
  id: "44444444-4444-4444-8444-444444444444",
  kind: "greenhouse",
  key: "acme",
  mode: "draft",
  proven_at: null,
  fetched_at: null,
  outcome: "new",
  code: "wt-k7m2p9x4qr",
};

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@firma.de",
    tenantId: "22222222-2222-4222-8222-222222222222",
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const draw = (onFetched = () => {}) => renderWithStore(<JobSources onFetched={onFetched} />, { auth: FOR_COMPANY });

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("JobSources", () => {
  it("shows the code and what it is for before anything is fetched", async () => {
    stubFetch(() => ({ body: [UNPROVEN] }));
    draw();

    expect(await screen.findByLabelText("Bestätigungscode")).toHaveTextContent("wt-k7m2p9x4qr");
    expect(screen.getByText(/Den Namen eines Boards kann jeder eintippen/)).toBeInTheDocument();
    expect(screen.getByText(/Bis dahin wird nichts übernommen/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Code prüfen" })).toBeInTheDocument();
  });

  it("checking the code reports what came in and reloads the adverts", async () => {
    let fetched = false;
    const onFetched = vi.fn();
    const calls = stubFetch((url, method) => {
      if (url.endsWith("/fetch") && method === "POST") {
        fetched = true;
        return { body: { source: { ...UNPROVEN, outcome: "code_missing" }, added: 0, changed: 0, closed: 0 } };
      }
      return { body: [fetched ? { ...UNPROVEN, outcome: "code_missing", fetched_at: "2026-10-18T09:00:00Z" } : UNPROVEN] };
    });
    draw(onFetched);

    await userEvent.click(await screen.findByRole("button", { name: "Code prüfen" }));

    expect(await screen.findByText("0 neu, 0 geändert, 0 geschlossen.")).toBeInTheDocument();
    expect(await screen.findByText(/Code im Board nicht gefunden/)).toBeInTheDocument();
    expect(calls.some((call) => call.url.endsWith(`/jobs/sources/${UNPROVEN.id}/fetch`))).toBe(true);
    expect(onFetched).toHaveBeenCalled();
  });

  it("sends kind, board name and mode, and reads a refused name in German", async () => {
    const calls = stubFetch((_url, method) =>
      method === "POST" ? problem(422, "The board name may hold only letters, digits and dashes") : { body: [] },
    );
    draw();

    const form = await screen.findByRole("form", { name: "Neue Quelle" });
    await userEvent.type(within(form).getByLabelText("Name des Boards"), "acme/../x");
    await userEvent.click(within(form).getByRole("button", { name: "Quelle hinzufügen" }));

    expect(
      await screen.findByText("Der Name des Boards darf nur Buchstaben, Ziffern und Bindestriche enthalten."),
    ).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/may hold only/);
    expect(calls.find((call) => call.method === "POST")?.body).toEqual({
      kind: "greenhouse",
      key: "acme/../x",
      mode: "draft",
    });
  });

  it("a member who removes a source reads who may", async () => {
    stubFetch((_url, method) => (method === "DELETE" ? problem(403, "not permitted") : { body: [UNPROVEN] }));
    draw();

    await userEvent.click(await screen.findByRole("button", { name: "Entfernen" }));
    await userEvent.click(await screen.findByRole("button", { name: /Wirklich entfernen/ }));

    expect(
      await screen.findByText("Das darf nur, wer Administrator dieses Unternehmens ist."),
    ).toBeInTheDocument();
  });
});
