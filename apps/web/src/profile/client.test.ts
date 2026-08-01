import { afterEach, describe, expect, it, vi } from "vitest";

import { PROFILE_BASE_URL } from "../env";
import {
  VISIBILITY_CAPABILITY,
  getMyProfile,
  getVisibility,
  saveMyProfile,
  setVisibility,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const input = {
  headline: "Senior Python",
  bio: "",
  location: "Berlin",
  remote_ok: true,
  skills: ["Python"],
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getMyProfile", () => {
  it("returns null when none exists yet — that is a state, not an error", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, null)));

    await expect(getMyProfile()).resolves.toBeNull();
  });

  it("returns null when nobody is logged in", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "not authenticated" })));

    await expect(getMyProfile()).resolves.toBeNull();
  });

  it("sends the cookie — the browser never sees the token itself", async () => {
    const fetchMock = vi.fn(async () => respond(200, null));
    vi.stubGlobal("fetch", fetchMock);

    await getMyProfile();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${PROFILE_BASE_URL}/profiles/me`);
    expect(init.credentials).toBe("include");
  });
});

describe("saveMyProfile", () => {
  it("never throws on a rejected form — it returns the failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        respond(422, { detail: [{ msg: "String should have at least 1 character" }] })
      )
    );

    const result = await saveMyProfile({ ...input, headline: "" });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message.length).toBeGreaterThan(0);
  });

  it("reports a lost session distinguishably, so the page can send you to the login", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "not authenticated" })));

    const result = await saveMyProfile(input);

    expect(result).toEqual({
      ok: false,
      reason: "unauthenticated",
      message: expect.any(String),
    });
  });

  it("does not send a visibility field — that belongs to the ledger", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, ...input, updated_at: "2026-08-02T10:00:00Z" })
    );
    vi.stubGlobal("fetch", fetchMock);

    await saveMyProfile(input);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const body = JSON.parse(String(init.body)) as Record<string, unknown>;
    expect(Object.keys(body).sort()).toEqual([
      "bio",
      "headline",
      "location",
      "remote_ok",
      "skills",
    ]);
  });
});

describe("visibility", () => {
  it("reads the ledger, not the profile", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, capability: VISIBILITY_CAPABILITY, granted: true })
    );
    vi.stubGlobal("fetch", fetchMock);

    await expect(getVisibility(SUBJECT)).resolves.toBe(true);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain("/consent/check");
    expect(JSON.parse(String(init.body))).toEqual({
      subject_id: SUBJECT,
      capability: VISIBILITY_CAPABILITY,
    });
  });

  it("treats an unreachable ledger as not visible — it never claims a release", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("Failed to fetch");
      })
    );

    await expect(getVisibility(SUBJECT)).resolves.toBe(false);
  });

  it("always sends a reason when withdrawing — the ledger demands one", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, capability: VISIBILITY_CAPABILITY, granted: false })
    );
    vi.stubGlobal("fetch", fetchMock);

    await setVisibility(SUBJECT, false);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain("/consent/revoke");
    const body = JSON.parse(String(init.body)) as Record<string, unknown>;
    expect(String(body.reason).length).toBeGreaterThan(0);
  });

  it("grants without a reason — a release needs no justification", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, capability: VISIBILITY_CAPABILITY, granted: true })
    );
    vi.stubGlobal("fetch", fetchMock);

    await setVisibility(SUBJECT, true);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain("/consent/grant");
    expect(JSON.parse(String(init.body))).not.toHaveProperty("reason");
  });
});
