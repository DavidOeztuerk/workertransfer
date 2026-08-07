import { afterEach, describe, expect, it, vi } from "vitest";

import { PORTFOLIO_BASE_URL } from "../env";
import {
  attachmentUrl,
  getMyPortfolio,
  getPortfolio,
  saveMyPortfolio,
  setPortfolioVisibility,
  uploadAttachment,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const item = {
  title: "Ein Werkzeug",
  summary: "",
  url: null,
  role: "",
  year: null,
  attachment: null,
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getMyPortfolio", () => {
  it("returns null when none exists yet", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, null)));

    await expect(getMyPortfolio()).resolves.toBeNull();
  });

  it("sends the cookie to the portfolio service", async () => {
    const fetchMock = vi.fn(async () => respond(200, null));
    vi.stubGlobal("fetch", fetchMock);

    await getMyPortfolio();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${PORTFOLIO_BASE_URL}/portfolios/me`);
    expect(init.credentials).toBe("include");
  });
});

describe("saveMyPortfolio", () => {
  it("sends exactly the one contract field", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, items: [item], updated_at: "2026-08-02T10:00:00Z" })
    );
    vi.stubGlobal("fetch", fetchMock);

    await saveMyPortfolio([item]);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(Object.keys(JSON.parse(String(init.body)))).toEqual(["items"]);
  });

  it("passes a rejected link through in the server's words", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(422, { detail: "Only http and https links are allowed" }))
    );

    const result = await saveMyPortfolio([{ ...item, url: "javascript:alert(1)" }]);

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).toContain("http");
  });
});

describe("visibility", () => {
  it("uses its own capability, not the profile's", async () => {
    // Sonst wäre "ich bin ansprechbar" stillschweigend auch "schaut euch meine
    // Arbeiten an".
    const fetchMock = vi.fn(async () =>
      respond(200, { subject_id: SUBJECT, capability: "x", granted: true })
    );
    vi.stubGlobal("fetch", fetchMock);

    await setPortfolioVisibility(SUBJECT, true);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body)).capability).toBe("portfolio.visibility:public");
  });
});

describe("getPortfolio", () => {
  it("turns 404 into 'nothing to show', because hidden and absent are the same case", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "No such portfolio" })));

    await expect(getPortfolio(SUBJECT)).resolves.toEqual({ ok: true, portfolio: null });
  });

  it("does not turn a silent ledger into 'nothing to show'", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "unavailable" })));

    await expect(getPortfolio(SUBJECT)).resolves.toMatchObject({ ok: false });
  });
});

describe("uploadAttachment", () => {
  it("sends multipart without setting the content type itself", async () => {
    // Den Rahmen samt boundary setzt der Browser; ihn von Hand zu setzen
    // erzeugt einen Header ohne boundary, und der Server kann nichts damit
    // anfangen.
    const fetchMock = vi.fn(async () =>
      respond(201, { name: "abc.png", content_type: "image/png", size: 4 })
    );
    vi.stubGlobal("fetch", fetchMock);

    await uploadAttachment(new File([new Uint8Array([1, 2, 3, 4])], "bild.png"));

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${PORTFOLIO_BASE_URL}/portfolios/me/attachments`);
    expect(init.body).toBeInstanceOf(FormData);
    expect(init.headers).toBeUndefined();
  });

  it("passes a refusal through instead of throwing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(422, { detail: "Only PNG, JPEG and PDF files are accepted" }))
    );

    const result = await uploadAttachment(new File(["x"], "x.txt"));

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).toContain("PNG");
  });
});

describe("attachmentUrl", () => {
  it("composes person and name, exactly like the server does", () => {
    expect(attachmentUrl(SUBJECT, "abc.png")).toBe(
      `${PORTFOLIO_BASE_URL}/portfolios/${SUBJECT}/attachments/abc.png`
    );
  });
});
