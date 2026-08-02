import { afterEach, describe, expect, it, vi } from "vitest";

import { API_BASE_URL } from "../env";
import {
  ALL_ON,
  getNotificationPreferences,
  saveNotificationPreferences,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getNotificationPreferences", () => {
  it("falls back to all-on, never to all-off", async () => {
    // Ein Netzfehler ist keine Abbestellung. Vier ausgeschaltete Schalter
    // würden beim nächsten Speichern geschrieben — und die Person hätte sich
    // abgemeldet, ohne es zu wollen.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new Error("offline");
      })
    );

    await expect(getNotificationPreferences()).resolves.toEqual(ALL_ON);
  });

  it("sends the cookie to the identity service", async () => {
    const fetchMock = vi.fn(async () => respond(200, ALL_ON));
    vi.stubGlobal("fetch", fetchMock);

    await getNotificationPreferences();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API_BASE_URL}/me/notification-preferences`);
    expect(init.credentials).toBe("include");
  });
});

describe("saveNotificationPreferences", () => {
  it("sends exactly the four flags", async () => {
    const fetchMock = vi.fn(async () => respond(200, ALL_ON));
    vi.stubGlobal("fetch", fetchMock);

    await saveNotificationPreferences({ ...ALL_ON, market_request: false });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      resume_request: true,
      market_request: false,
      application_update: true,
      transfer_update: true,
    });
  });

  it("does not throw when the session has expired", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "no" })));

    await expect(saveNotificationPreferences(ALL_ON)).resolves.toMatchObject({ ok: false });
  });
});
