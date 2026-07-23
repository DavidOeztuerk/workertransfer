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
      tenantId: "11111111-1111-1111-1111-111111111111",
    });
    expect(r).toEqual({ ok: true });
  });

  it("returns a german message on 401", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "invalid credentials" }, 401)));
    const r = await login({
      email: "a@b.com",
      password: "wrong",
      tenantId: "11111111-1111-1111-1111-111111111111",
    });
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.message.length).toBeGreaterThan(0);
  });

  it("sends tenant_id (snake_case) and posts with credentials", async () => {
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
      tenantId: "11111111-1111-1111-1111-111111111111",
    });
    expect(calls).toHaveLength(1);
    expect(calls[0]?.url).toMatch(/\/auth\/login$/);
    const body = String(calls[0]?.init.body ?? "");
    expect(body).toContain('"tenant_id"');
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
