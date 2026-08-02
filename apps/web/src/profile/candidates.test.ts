import { afterEach, describe, expect, it, vi } from "vitest";

import { PROFILE_BASE_URL } from "../env";
import { listCandidates } from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const page = { items: [], next_cursor: null };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("listCandidates", () => {
  it("asks the profile service with the session cookie", async () => {
    const fetchMock = vi.fn(async () => respond(200, page));
    vi.stubGlobal("fetch", fetchMock);

    await listCandidates();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${PROFILE_BASE_URL}/profiles`);
    expect(init.credentials).toBe("include");
  });

  it("passes the cursor through so the next page continues where this one ended", async () => {
    const fetchMock = vi.fn(async () => respond(200, page));
    vi.stubGlobal("fetch", fetchMock);

    await listCandidates("abc123");

    const [url] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${PROFILE_BASE_URL}/profiles?cursor=abc123`);
  });

  it("reports a missing company distinguishably — that is a fixable state", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(403, { detail: "reading other profiles requires an active company" }))
    );

    const result = await listCandidates();

    expect(result).toEqual({ ok: false, reason: "no-company", message: expect.any(String) });
  });

  it("does not turn an empty page into an error", async () => {
    // Eine leere Seite heißt nicht "kaputt", sondern "niemand hat freigegeben".
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, page)));

    const result = await listCandidates();

    expect(result).toEqual({ ok: true, items: [], nextCursor: null });
  });

  it("passes a silent ledger through as its own state, not as an empty result", async () => {
    // 503 heißt "wir wissen es nicht". Als leere Liste anzuzeigen wäre die
    // Behauptung, niemand habe freigegeben — genau das weiß hier niemand.
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "consent-service unreachable" })));

    const result = await listCandidates();

    expect(result).toEqual({ ok: false, reason: "consent-unavailable", message: expect.any(String) });
  });
});
