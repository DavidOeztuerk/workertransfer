import { afterEach, describe, expect, it, vi } from "vitest";

import { API_BASE_URL } from "../env";
import { requestErasure } from "./client";

function respond(status: number, body: unknown = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("requestErasure", () => {
  it("asks identity-service and sends nothing but the cookie", async () => {
    // Kein Rumpf, insbesondere KEIN Grund: von einem Menschen, der sein Konto
    // löschen will, eine Begründung zu verlangen, wäre ein Hebel gegen ihn
    // (ADR-0027 §Kontext 5). Und keine `subject_id`: wer nichts angeben kann,
    // kann nichts fälschen.
    const fetchMock = vi.fn(async () => respond(202, { retained: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await requestErasure();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API_BASE_URL}/account/erasure`);
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(init.body).toBeUndefined();
  });

  it("reports success on 202 — accepted, not finished", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(202, { retained: [] })));

    await expect(requestErasure()).resolves.toEqual({ ok: true });
  });

  it("never throws on a rejected request", async () => {
    // Wie `LoginResult`: ein Fehlschlag ist ein Zustand, den die Seite zeigt,
    // keine Ausnahme, die irgendwo hochblubbert.
    vi.stubGlobal("fetch", vi.fn(async () => respond(500)));

    const result = await requestErasure();

    expect(result.ok).toBe(false);
  });

  it("says the session expired on 401 instead of a generic failure", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401)));

    const result = await requestErasure();

    expect(result).toEqual({
      ok: false,
      message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an.",
    });
  });

  it("survives having no connection at all", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new Error("offline");
      })
    );

    const result = await requestErasure();

    expect(result).toEqual({ ok: false, message: "Keine Verbindung zum Server." });
  });
});
