import { afterEach, describe, expect, it, vi } from "vitest";

import { TRANSFER_BASE_URL } from "../env";
import {
  companyMove,
  expressInterest,
  listCompanyTransfers,
  listMyTransfers,
  makeOffer,
  personMove,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TRANSFER = "33333333-3333-3333-3333-333333333333";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("expressInterest", () => {
  it("says nothing about whether the person exists or is listening", async () => {
    // Kein Status, keine Freigabe und „gerade nicht" sind derselbe Fall — sonst
    // wäre die Oberfläche das Orakel, das der Server gerade verweigert hat.
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "No such person" })));

    const result = await expressInterest(SUBJECT, "Hallo");

    expect(result).toMatchObject({ ok: false, reason: "not-available" });
    if (!result.ok) {
      expect(result.message).not.toMatch(/existiert|unbekannt|nicht freigegeben/i);
    }
  });

  it("tells a company when a conversation is already running", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "already running" })));

    await expect(expressInterest(SUBJECT, "")).resolves.toMatchObject({
      ok: false,
      reason: "conflict",
    });
  });
});

describe("personMove", () => {
  it("has one path per transition, because each belongs to one side", async () => {
    const fetchMock = vi.fn(async () => respond(200, { id: TRANSFER, status: "talking" }));
    vi.stubGlobal("fetch", fetchMock);

    await personMove(TRANSFER, "accept-talk");
    await personMove(TRANSFER, "accept-offer");
    await personMove(TRANSFER, "confirm-release");
    await personMove(TRANSFER, "decline");
    await companyMove(TRANSFER, "complete");
    await companyMove(TRANSFER, "withdraw");

    const paths = fetchMock.mock.calls.map((call) => String((call as unknown[])[0]));
    expect(paths).toEqual([
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/accept-talk`,
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/accept-offer`,
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/confirm-release`,
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/decline`,
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/complete`,
      `${TRANSFER_BASE_URL}/transfers/${TRANSFER}/withdraw`,
    ]);
  });

  it("reports a refused transition instead of pretending it worked", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "not allowed from accepted" })));

    await expect(personMove(TRANSFER, "accept-talk")).resolves.toMatchObject({ ok: false });
  });
});

describe("makeOffer", () => {
  it("sends the fee in cents and omits nothing the contract expects", async () => {
    const fetchMock = vi.fn(async () => respond(200, { id: TRANSFER, status: "offered" }));
    vi.stubGlobal("fetch", fetchMock);

    await makeOffer(TRANSFER, { note: "Gutes Angebot", start_on: "2026-11", fee_cents: 500000 });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      note: "Gutes Angebot",
      start_on: "2026-11",
      fee_cents: 500000,
    });
  });

  it("sends null rather than an empty string for an omitted start", async () => {
    // "" wäre kein Monat und würde am Muster des Vertrags scheitern — mit einer
    // Fehlermeldung, die nach einem Serverproblem aussieht.
    const fetchMock = vi.fn(async () => respond(200, { id: TRANSFER, status: "offered" }));
    vi.stubGlobal("fetch", fetchMock);

    await makeOffer(TRANSFER, { note: "", start_on: null, fee_cents: null });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ note: "", start_on: null, fee_cents: null });
  });
});

describe("the lists", () => {
  it("reads the person list and the company list from different paths", async () => {
    const fetchMock = vi.fn(async () => respond(200, []));
    vi.stubGlobal("fetch", fetchMock);

    await listMyTransfers();
    await listCompanyTransfers();

    const paths = fetchMock.mock.calls.map((call) => String((call as unknown[])[0]));
    expect(paths).toEqual([
      `${TRANSFER_BASE_URL}/transfers/me`,
      `${TRANSFER_BASE_URL}/transfers`,
    ]);
  });

  it("does not turn an error into an empty list", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(500, { detail: "boom" })));

    await expect(listMyTransfers()).resolves.toMatchObject({ ok: false });
  });
});
