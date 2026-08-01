import { afterEach, describe, expect, it, vi } from "vitest";

import { fetchMe, login } from "./client";

const ok = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });

afterEach(() => vi.restoreAllMocks());

describe("login", () => {
  it("returns ok on 200", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ status: "ok" })));
    const r = await login({
      email: "a@b.com",
      password: "strongpassword1",
    });
    expect(r).toEqual({ ok: true });
  });

  it("returns a german message on 401", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "invalid credentials" }, 401)));
    const r = await login({
      email: "a@b.com",
      password: "wrong",
    });
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.message.length).toBeGreaterThan(0);
  });

  it("posts with credentials and sends no tenant", async () => {
    const calls: Array<{ url: string; init: RequestInit }> = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
        calls.push({ url: String(url), init: init ?? {} });
        return ok({ status: "ok" });
      })
    );
    await login({
      email: "a@b.com",
      password: "strongpassword1",
    });
    expect(calls).toHaveLength(1);
    expect(calls[0]?.url).toMatch(/\/auth\/login$/);
    const body = String(calls[0]?.init.body ?? "");
    // A tenant is a company and is never chosen at login (ADR-0017).
    expect(body).not.toContain('"tenant_id"');
    expect(String(calls[0]?.init.credentials)).toBe("include");
  });
});

describe("fetchMe", () => {
  it("returns null on 401", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "no" }, 401)));
    expect(await fetchMe()).toBeNull();
  });

  it("returns the principal on 200", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ok({ user_id: "u", tenant_id: "t", roles: ["user"] }))
    );
    const me = await fetchMe();
    expect(me?.tenant_id).toBe("t");
  });
});
