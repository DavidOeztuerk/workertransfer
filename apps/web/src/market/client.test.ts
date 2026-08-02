import { afterEach, describe, expect, it, vi } from "vitest";

import { TRANSFER_BASE_URL } from "../env";
import {
  answerMarketRequest,
  getMyMarketStatus,
  listCompanyMarketRequests,
  listMyMarketRequests,
  requestMarketStatus,
  revokeMarketAccess,
  saveMyMarketStatus,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const REQUEST_ID = "22222222-2222-2222-2222-222222222222";
const status = {
  subject_id: SUBJECT,
  availability: "listening",
  employed: true,
  note: "",
  is_approachable: true,
  updated_at: "2026-08-02T12:00:00Z",
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getMyMarketStatus", () => {
  it("falls back to unavailable when the server cannot be reached", async () => {
    // Nicht `null` und nicht „offen": die Voreinstellung darf nie zugunsten
    // des Marktes ausfallen. Wer nichts gesagt hat, hat nicht „ich höre zu"
    // gesagt — und ein Netzfehler ist erst recht keine Zustimmung.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new Error("offline");
      })
    );

    await expect(getMyMarketStatus()).resolves.toMatchObject({
      availability: "unavailable",
      is_approachable: false,
    });
  });

  it("sends the cookie to the transfer service", async () => {
    const fetchMock = vi.fn(async () => respond(200, status));
    vi.stubGlobal("fetch", fetchMock);

    await getMyMarketStatus();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${TRANSFER_BASE_URL}/market/me`);
    expect(init.credentials).toBe("include");
  });
});

describe("saveMyMarketStatus", () => {
  it("sends exactly the three fields of the contract", async () => {
    const fetchMock = vi.fn(async () => respond(200, status));
    vi.stubGlobal("fetch", fetchMock);

    await saveMyMarketStatus({ availability: "open", employed: false, note: "Suche aktiv" });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      availability: "open",
      employed: false,
      note: "Suche aktiv",
    });
  });

  it("does not throw when the session has expired", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "not authenticated" })));

    await expect(saveMyMarketStatus({ availability: "open", employed: false, note: "" })).resolves
      .toMatchObject({ ok: false });
  });
});

describe("requestMarketStatus", () => {
  it("tells a company it has already asked", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "already asked" })));

    await expect(requestMarketStatus(SUBJECT)).resolves.toMatchObject({
      ok: false,
      reason: "already-asked",
    });
  });

  it("says nothing about whether the person exists", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "No such market status" })));

    const result = await requestMarketStatus(SUBJECT);

    expect(result).toMatchObject({ ok: false, reason: "not-available" });
    if (!result.ok) {
      expect(result.message).not.toMatch(/existiert|gibt es nicht|unbekannt/i);
    }
  });

  it("keeps the ledger outage apart from a refusal", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "ledger down" })));

    await expect(requestMarketStatus(SUBJECT)).resolves.toMatchObject({
      ok: false,
      reason: "unavailable",
    });
  });
});

describe("answerMarketRequest", () => {
  it("uses two paths, because granting and declining are different acts", async () => {
    const fetchMock = vi.fn(async () => respond(200, { id: REQUEST_ID, status: "GRANTED" }));
    vi.stubGlobal("fetch", fetchMock);

    await answerMarketRequest(REQUEST_ID, true);
    await answerMarketRequest(REQUEST_ID, false);
    await revokeMarketAccess(REQUEST_ID);

    const paths = fetchMock.mock.calls.map((call) => String((call as unknown[])[0]));
    expect(paths).toEqual([
      `${TRANSFER_BASE_URL}/market/requests/${REQUEST_ID}/grant`,
      `${TRANSFER_BASE_URL}/market/requests/${REQUEST_ID}/decline`,
      `${TRANSFER_BASE_URL}/market/requests/${REQUEST_ID}/revoke`,
    ]);
  });
});

describe("listMyMarketRequests", () => {
  it("does not turn a silent ledger into an empty list", async () => {
    // Eine leere Liste wäre die Behauptung, niemand habe gefragt — und das
    // weiß gerade niemand.
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "ledger down" })));

    await expect(listMyMarketRequests()).resolves.toMatchObject({ ok: false });
  });

  it("reads the person list and the company list from different paths", async () => {
    const fetchMock = vi.fn(async () => respond(200, []));
    vi.stubGlobal("fetch", fetchMock);

    await listMyMarketRequests();
    await listCompanyMarketRequests();

    const paths = fetchMock.mock.calls.map((call) => String((call as unknown[])[0]));
    expect(paths).toEqual([
      `${TRANSFER_BASE_URL}/market/me/requests`,
      `${TRANSFER_BASE_URL}/market/requests`,
    ]);
  });
});
