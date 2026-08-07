import { afterEach, describe, expect, it, vi } from "vitest";

import { RESUME_BASE_URL } from "../env";
import {
  answerRequest,
  getMyResume,
  getResume,
  listMyRequests,
  requestResume,
  revokeAccess,
  saveMyResume,
} from "./client";

function respond(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const REQUEST_ID = "22222222-2222-2222-2222-222222222222";
const position = {
  employer: "Acme GmbH",
  title: "Backend-Entwicklerin",
  started_on: "2020-01",
  ended_on: null,
  description: "",
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getMyResume", () => {
  it("returns null when none exists yet — that is a state, not an error", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(200, null)));

    await expect(getMyResume()).resolves.toBeNull();
  });

  it("sends the cookie to the resume service", async () => {
    const fetchMock = vi.fn(async () => respond(200, null));
    vi.stubGlobal("fetch", fetchMock);

    await getMyResume();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${RESUME_BASE_URL}/resumes/me`);
    expect(init.credentials).toBe("include");
  });
});

describe("saveMyResume", () => {
  it("never throws on a rejected form — it returns the failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(422, { detail: "Only one position may be left open" }))
    );

    const result = await saveMyResume({ positions: [position], education: [] });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.message).toContain("one position");
  });

  it("sends exactly the two contract fields", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, {
        subject_id: SUBJECT,
        positions: [position],
        education: [],
        updated_at: "2026-08-02T10:00:00Z",
      })
    );
    vi.stubGlobal("fetch", fetchMock);

    await saveMyResume({ positions: [position], education: [] });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(Object.keys(JSON.parse(String(init.body))).sort()).toEqual(["education", "positions"]);
  });
});

describe("requestResume", () => {
  it("distinguishes a company that already asked", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(409, { detail: "This company has already asked" }))
    );

    const result = await requestResume(SUBJECT);

    expect(result).toEqual({ ok: false, reason: "already-asked", message: expect.any(String) });
  });

  it("distinguishes a missing company from a missing person", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(403, { detail: "needs a company" })));
    await expect(requestResume(SUBJECT)).resolves.toMatchObject({ reason: "no-company" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "No such resume" })));
    await expect(requestResume(SUBJECT)).resolves.toMatchObject({ reason: "not-available" });
  });
});

describe("answering", () => {
  it("posts to grant or decline, never to a shared endpoint with a flag", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { id: REQUEST_ID, subject_id: SUBJECT, tenant_id: "t", status: "GRANTED", created_at: "2026-08-02T10:00:00Z" })
    );
    vi.stubGlobal("fetch", fetchMock);

    await answerRequest(REQUEST_ID, true);
    await answerRequest(REQUEST_ID, false);

    const urls = fetchMock.mock.calls.map((call) => String((call as unknown as [string])[0]));
    expect(urls[0]).toContain(`/resumes/requests/${REQUEST_ID}/grant`);
    expect(urls[1]).toContain(`/resumes/requests/${REQUEST_ID}/decline`);
  });

  it("never builds a capability string — that belongs to the server", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { id: REQUEST_ID, subject_id: SUBJECT, tenant_id: "t", status: "GRANTED", created_at: "2026-08-02T10:00:00Z" })
    );
    vi.stubGlobal("fetch", fetchMock);

    await answerRequest(REQUEST_ID, true);
    await revokeAccess(REQUEST_ID);

    for (const call of fetchMock.mock.calls) {
      const [, init] = call as unknown as [string, RequestInit];
      expect(String(init?.body ?? "")).not.toContain("resume.visibility");
    }
  });
});

describe("listMyRequests", () => {
  it("keeps status and active apart — they can disagree", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        respond(200, [
          {
            id: REQUEST_ID,
            subject_id: SUBJECT,
            tenant_id: "t",
            status: "GRANTED",
            created_at: "2026-08-02T10:00:00Z",
            active: false,
          },
        ])
      )
    );

    const result = await listMyRequests();

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.requests[0]?.status).toBe("GRANTED");
      expect(result.requests[0]?.active).toBe(false);
    }
  });

  it("reports a silent ledger instead of showing an empty list", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "consent ledger unavailable" })));

    await expect(listMyRequests()).resolves.toMatchObject({ ok: false });
  });
});

describe("getResume", () => {
  it("returns null for a resume that is missing or withheld — the same case", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "No such resume" })));

    await expect(getResume(SUBJECT)).resolves.toEqual({ ok: true, resume: null });
  });

  it("does not turn a silent ledger into 'no resume'", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(503, { detail: "consent ledger unavailable" })));

    await expect(getResume(SUBJECT)).resolves.toMatchObject({ ok: false });
  });
});
