import { afterEach, describe, expect, it, vi } from "vitest";

import { APPLICATIONS_BASE_URL } from "../env";
import { advanceApplication, apply, listMyApplications } from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const JOB = "11111111-1111-1111-1111-111111111111";
const input = { job_id: JOB, message: "Passt.", shares_resume: true, shares_portfolio: false };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("apply", () => {
  it("sends what is shared — the profile is not a choice", async () => {
    const fetchMock = vi.fn(async () => respond(201, { id: "a", job_id: JOB }));
    vi.stubGlobal("fetch", fetchMock);

    await apply(input);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${APPLICATIONS_BASE_URL}/applications`);
    const body = JSON.parse(String(init.body)) as Record<string, unknown>;
    expect(Object.keys(body).sort()).toEqual([
      "job_id",
      "message",
      "shares_portfolio",
      "shares_resume",
    ]);
    expect(body).not.toHaveProperty("shares_profile");
  });

  it("keeps four refusals apart, because each needs a different reaction", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "no" })));
    await expect(apply(input)).resolves.toMatchObject({ reason: "unauthenticated" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "no" })));
    await expect(apply(input)).resolves.toMatchObject({ reason: "gone" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "already" })));
    await expect(apply(input)).resolves.toMatchObject({ reason: "already" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "down" })));
    await expect(apply(input)).resolves.toMatchObject({ reason: "unavailable" });
  });

  it("does not turn a silent dependency into a rejection", async () => {
    // Eine Absage, die niemand ausgesprochen hat, wäre die schlimmere Antwort.
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "down" })));

    const result = await apply(input);

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).not.toMatch(/abgelehnt|nicht möglich/i);
  });
});

describe("listMyApplications", () => {
  it("treats 'not logged in' as an empty list, not as a failure", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "no" })));

    await expect(listMyApplications()).resolves.toEqual({ ok: true, applications: [] });
  });
});

describe("advanceApplication", () => {
  it("posts the status the company owns", async () => {
    const fetchMock = vi.fn(async () => respond(200, { id: "a", status: "hired" }));
    vi.stubGlobal("fetch", fetchMock);

    await advanceApplication("a", "hired");

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${APPLICATIONS_BASE_URL}/applications/a/status`);
    expect(JSON.parse(String(init.body))).toEqual({ status: "hired" });
  });
});
