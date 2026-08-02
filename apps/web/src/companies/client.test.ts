import { afterEach, describe, expect, it, vi } from "vitest";

import { COMPANIES_BASE_URL } from "../env";
import { getCompanyProfile, getOwnCompanyProfile, saveCompanyProfile } from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const TENANT = "11111111-1111-1111-1111-111111111111";
const input = {
  display_name: "Muster",
  about: "",
  website: null,
  locations: [],
  benefits: [],
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getCompanyProfile", () => {
  it("turns 404 into null — a company without a profile stays anonymous", async () => {
    // Kein Fehler, sondern ein Zustand, den das Unternehmen selbst
    // herbeigeführt hat.
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "none" })));

    await expect(getCompanyProfile(TENANT)).resolves.toBeNull();
  });

  it("asks the public endpoint", async () => {
    const fetchMock = vi.fn(async () => respond(200, { tenant_id: TENANT }));
    vi.stubGlobal("fetch", fetchMock);

    await getCompanyProfile(TENANT);

    expect((fetchMock.mock.calls[0] as unknown as [string])[0]).toBe(
      `${COMPANIES_BASE_URL}/companies/${TENANT}/profile`
    );
  });
});

describe("getOwnCompanyProfile", () => {
  it("keeps 'null' apart from 'not asked' — the server sends null for 'none yet'", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, null)));

    await expect(getOwnCompanyProfile()).resolves.toBeNull();
  });
});

describe("saveCompanyProfile", () => {
  it("sends no tenant — the company comes from the token", async () => {
    const fetchMock = vi.fn(async () => respond(200, { tenant_id: TENANT, ...input }));
    vi.stubGlobal("fetch", fetchMock);

    await saveCompanyProfile(input);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).not.toHaveProperty("tenant_id");
  });

  it("passes a rejected link through in the server's words", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(422, { detail: "Only http and https links are allowed" }))
    );

    const result = await saveCompanyProfile({ ...input, website: "javascript:alert(1)" });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).toContain("http");
  });

  it("names a missing company as its own case", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(403, { detail: "needs a company" })));

    await expect(saveCompanyProfile(input)).resolves.toMatchObject({ reason: "no-company" });
  });
});
