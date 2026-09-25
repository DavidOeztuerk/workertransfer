import { afterEach, describe, expect, it, vi } from "vitest";

import { onSessionExpired, request } from "./client";
import { resetForTests } from "./refreshGate";

afterEach(() => {
  vi.unstubAllGlobals();
  resetForTests();
});

function respondWith(sequence: { status: number; body?: unknown; path?: string }[]) {
  const asked: string[] = [];
  let i = 0;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      asked.push(new URL(url).pathname);
      const answer = sequence[i] ?? sequence[sequence.length - 1];
      i += 1;
      return new Response(JSON.stringify(answer?.body ?? {}), {
        status: answer?.status ?? 200,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return asked;
}

describe("request — session renewal", () => {
  it("refreshes once on 401 and repeats the original call", async () => {
    const paths = respondWith([
      { status: 401 },
      { status: 200, body: { status: "ok" } },
      { status: 200, body: { items: [] } },
    ]);

    const answer = await request<{ items: unknown[] }>(
      "http://example.test",
      "/jobs",
    );

    expect(answer.ok).toBe(true);
    expect(paths).toEqual(["/jobs", "/auth/refresh", "/jobs"]);
  });

  it("two parallel 401s share one refresh", async () => {
    let refresh = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: string | URL | Request) => {
        const path = new URL(typeof input === "string" ? input : input.toString())
          .pathname;
        if (path === "/auth/refresh") {
          refresh += 1;
          return new Response(JSON.stringify({ status: "ok" }), { status: 200 });
        }
        if (refresh === 0) {
          return new Response("{}", { status: 401 });
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      }),
    );

    const [a, b] = await Promise.all([
      request("http://example.test", "/jobs"),
      request("http://example.test", "/profiles/me"),
    ]);

    expect(a.ok).toBe(true);
    expect(b.ok).toBe(true);
    expect(refresh).toBe(1);
  });

  it("a 401 on /auth/login triggers no refresh", async () => {
    const paths = respondWith([{ status: 401, body: { title: "no" } }]);

    const answer = await request(
      "http://example.test",
      "/auth/login",
      { method: "POST", body: { email: "a@b.de", password: "x" } },
    );

    expect(answer.ok).toBe(false);
    expect(paths).toEqual(["/auth/login"]);
  });

  it("notifies about session expiry when a 401 cannot be renewed", async () => {
    const expirySpy = vi.fn();
    const unsubscribe = onSessionExpired(expirySpy);

    try {
      respondWith([
        { status: 401 }, // the first call fails with 401
        { status: 401 }, // POST /auth/refresh fails too (token dead)
      ]);

      const answer = await request("http://example.test", "/profiles/me");
      expect(answer.ok).toBe(false);
      expect(expirySpy).toHaveBeenCalledTimes(1);
    } finally {
      unsubscribe();
    }
  });

  it("puts no token into storage", async () => {
    respondWith([{ status: 200, body: {} }]);
    await request("http://example.test", "/auth/session");
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });
});
