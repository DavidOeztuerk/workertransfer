import { afterEach, describe, expect, it, vi } from "vitest";

import { JOBS_BASE_URL } from "../env";
import { closeJob, createJob, listOwnJobs, publishJob, searchJobs } from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const JOB = "11111111-1111-1111-1111-111111111111";
const input = {
  title: "Backend-Entwicklerin",
  description: "Was zu tun ist.",
  location: "Berlin",
  remote: "hybrid" as const,
  employment: "full_time" as const,
  skills: ["Python"],
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("searchJobs", () => {
  it("omits empty filters instead of sending them empty", async () => {
    // `remote=` läse der Server als Filter auf einen leeren Wert und fände
    // nichts.
    const fetchMock = vi.fn(async () => respond(200, { items: [], next_cursor: null }));
    vi.stubGlobal("fetch", fetchMock);

    await searchJobs({ q: "python", remote: "", location: "" });

    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe(`${JOBS_BASE_URL}/jobs?q=python`);
  });

  it("asks without any filter at all when none is given", async () => {
    const fetchMock = vi.fn(async () => respond(200, { items: [], next_cursor: null }));
    vi.stubGlobal("fetch", fetchMock);

    await searchJobs();

    expect((fetchMock.mock.calls[0] as unknown as [string])[0]).toBe(`${JOBS_BASE_URL}/jobs`);
  });

  it("does not turn an empty result into an error", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, { items: [], next_cursor: null })));

    await expect(searchJobs()).resolves.toEqual({ ok: true, items: [], nextCursor: null });
  });
});

describe("createJob", () => {
  it("sends no tenant — the company comes from the token", async () => {
    const fetchMock = vi.fn(async () => respond(201, { id: JOB, ...input, status: "draft" }));
    vi.stubGlobal("fetch", fetchMock);

    await createJob(input);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).not.toHaveProperty("tenant_id");
  });

  it("names a missing company as its own case", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(403, { detail: "needs a company" })));

    await expect(createJob(input)).resolves.toMatchObject({ reason: "no-company" });
  });
});

describe("publishJob / closeJob", () => {
  it("keeps a wrong state apart from a wrong form", async () => {
    // 409 heißt: die Eingabe ist in Ordnung, der Zustand passt nicht. Die Seite
    // sagt das anders als einen Formularfehler.
    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "A closed job cannot" })));

    await expect(publishJob(JOB)).resolves.toMatchObject({ reason: "conflict" });
  });

  it("posts to the right path", async () => {
    const fetchMock = vi.fn(async () => respond(200, { id: JOB, ...input, status: "closed" }));
    vi.stubGlobal("fetch", fetchMock);

    await closeJob(JOB);

    expect((fetchMock.mock.calls[0] as unknown as [string])[0]).toBe(
      `${JOBS_BASE_URL}/jobs/${JOB}/close`
    );
  });
});

describe("listOwnJobs", () => {
  it("treats a missing company as an empty list, not as a failure", async () => {
    // Kein aktives Unternehmen ist ein behebbarer Zustand.
    vi.stubGlobal("fetch", vi.fn(async () => respond(403, { detail: "needs a company" })));

    await expect(listOwnJobs()).resolves.toEqual({ ok: true, jobs: [] });
  });
});
