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
  it("returns the principal on 200", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ok({ user_id: "u", email: "a@b.com", tenant_id: "t", roles: ["user"] }))
    );
    const me = await fetchMe();
    expect(me?.tenant_id).toBe("t");
  });

  it("erneuert die Sitzung, wenn nur das Access-Token abgelaufen ist", async () => {
    // Der eigentliche Fall, und er war ungedeckt: das Access-Token lebt 15
    // Minuten, das Refresh-Token 24 Stunden (ADR-0007). Ohne diesen Weg ist
    // man eine Viertelstunde nach dem Anmelden abgemeldet — lautlos, mitten im
    // Ausfüllen eines Formulars.
    const wege: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: RequestInfo | URL) => {
        const pfad = String(url);
        wege.push(pfad);
        if (pfad.endsWith("/auth/refresh")) return ok({ status: "ok" });
        // Erst nach dem Erneuern antwortet /me.
        const schonErneuert = wege.some((w) => w.endsWith("/auth/refresh"));
        return schonErneuert
          ? ok({ user_id: "u", email: "a@b.com", tenant_id: null, roles: ["user"] })
          : ok({ detail: "not authenticated" }, 401);
      })
    );

    const me = await fetchMe();

    expect(me?.user_id).toBe("u");
    expect(wege.filter((w) => w.endsWith("/auth/refresh"))).toHaveLength(1);
  });

  it("gibt null zurück, wenn auch das Refresh-Token nicht mehr trägt", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ok({ detail: "invalid credentials" }, 401)));
    expect(await fetchMe()).toBeNull();
  });

  it("versucht das Erneuern genau EINMAL, auch bei gleichzeitigen Aufrufen", async () => {
    // Der Refresh rotiert die jti: das alte Token wird entwertet, ein neues
    // ausgegeben (ADR-0008). Zwei gleichzeitige Erneuerungen hiessen deshalb,
    // dass die zweite mit einem bereits entwerteten Token ankommt — und die
    // Sitzung genau dadurch verliert, was sie retten sollte. `my-data.tsx`
    // ruft fetchMe neben der Sitzungsabfrage auf, der Fall ist also real.
    const wege: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: RequestInfo | URL) => {
        const pfad = String(url);
        wege.push(pfad);
        if (pfad.endsWith("/auth/refresh")) {
          await new Promise((r) => setTimeout(r, 10));
          return ok({ status: "ok" });
        }
        const schonErneuert = wege.some((w) => w.endsWith("/auth/refresh"));
        return schonErneuert
          ? ok({ user_id: "u", email: "a@b.com", tenant_id: null, roles: ["user"] })
          : ok({ detail: "not authenticated" }, 401);
      })
    );

    const [a, b, c] = await Promise.all([fetchMe(), fetchMe(), fetchMe()]);

    expect(wege.filter((w) => w.endsWith("/auth/refresh"))).toHaveLength(1);
    expect(a?.user_id).toBe("u");
    expect(b?.user_id).toBe("u");
    expect(c?.user_id).toBe("u");
  });

  it("erneuert nicht bei anderen Fehlern als 401", async () => {
    // Ein 503 heisst "der Dienst schweigt", nicht "deine Sitzung ist alt".
    // Darauf zu erneuern hiesse, bei jeder Störung Token zu rotieren.
    const wege: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: RequestInfo | URL) => {
        wege.push(String(url));
        return ok({ detail: "unavailable" }, 503);
      })
    );
    expect(await fetchMe()).toBeNull();
    expect(wege.filter((w) => w.endsWith("/auth/refresh"))).toHaveLength(0);
  });
});
