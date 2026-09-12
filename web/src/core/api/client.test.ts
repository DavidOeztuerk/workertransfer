import { afterEach, describe, expect, it, vi } from "vitest";

import { onSessionExpired, request } from "./client";
import { zuruecksetzenFuerTests } from "./refreshGate";

afterEach(() => {
  vi.unstubAllGlobals();
  zuruecksetzenFuerTests();
});

function antworten(folge: { status: number; body?: unknown; path?: string }[]) {
  const gefragt: string[] = [];
  let i = 0;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      gefragt.push(new URL(url).pathname);
      const answer = folge[i] ?? folge[folge.length - 1];
      i += 1;
      return new Response(JSON.stringify(answer?.body ?? {}), {
        status: answer?.status ?? 200,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return gefragt;
}

describe("request — Sitzungserneuerung", () => {
  it("erneuert einmal bei 401 und wiederholt den Originalaufruf", async () => {
    const pfade = antworten([
      { status: 401 },
      { status: 200, body: { status: "ok" } },
      { status: 200, body: { items: [] } },
    ]);

    const answer = await request<{ items: unknown[] }>(
      "http://example.test",
      "/jobs",
    );

    expect(answer.ok).toBe(true);
    expect(pfade).toEqual(["/jobs", "/auth/refresh", "/jobs"]);
  });

  it("zwei parallele 401 teilen sich einen Refresh", async () => {
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

  it("401 auf /auth/login löst keinen Refresh aus", async () => {
    const pfade = antworten([{ status: 401, body: { title: "no" } }]);

    const answer = await request(
      "http://example.test",
      "/auth/login",
      { method: "POST", body: { email: "a@b.de", password: "x" } },
    );

    expect(answer.ok).toBe(false);
    expect(pfade).toEqual(["/auth/login"]);
  });

  it("benachrichtigt über Sitzungsablauf, wenn 401 nicht erneuert werden kann", async () => {
    const ablaufSpion = vi.fn();
    const abmelden = onSessionExpired(ablaufSpion);

    try {
      antworten([
        { status: 401 }, // Erstaufruf scheitert mit 401
        { status: 401 }, // POST /auth/refresh scheitert ebenfalls (Token tot)
      ]);

      const answer = await request("http://example.test", "/profiles/me");
      expect(answer.ok).toBe(false);
      expect(ablaufSpion).toHaveBeenCalledTimes(1);
    } finally {
      abmelden();
    }
  });

  it("legt kein Token in den Speicher", async () => {
    antworten([{ status: 200, body: {} }]);
    await request("http://example.test", "/auth/session");
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });
});
