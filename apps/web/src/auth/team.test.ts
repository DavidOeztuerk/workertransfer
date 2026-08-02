import { afterEach, describe, expect, it, vi } from "vitest";

import { API_BASE_URL } from "../env";
import {
  acceptInvitation,
  inviteMember,
  listInvitations,
  listMembers,
  removeMember,
  withdrawInvitation,
} from "./team";

function respond(status: number, body: unknown): Response {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const TENANT = "11111111-1111-1111-1111-111111111111";
const INVITATION = "22222222-2222-2222-2222-222222222222";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("listMembers", () => {
  it("asks identity-service with the session cookie", async () => {
    const fetchMock = vi.fn(async () => respond(200, []));
    vi.stubGlobal("fetch", fetchMock);

    await listMembers(TENANT);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API_BASE_URL}/companies/${TENANT}/members`);
    expect(init.credentials).toBe("include");
  });

  it("treats 404 as 'not yours' rather than as a crash", async () => {
    // Der Server antwortet absichtlich 404 statt 403, damit niemand erfragen
    // kann, welche Unternehmen es gibt. Die Oberfläche muss daraus einen
    // ruhigen Zustand machen, keinen Fehler.
    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "no such company" })));

    await expect(listMembers(TENANT)).resolves.toEqual({ ok: true, members: [] });
  });
});

describe("inviteMember", () => {
  it("sends only the address and the role — never the company from the body", async () => {
    const fetchMock = vi.fn(async () =>
      respond(201, {
        id: INVITATION,
        email: "neu@firma.example",
        role: "member",
        status: "pending",
        created_at: "2026-08-02T10:00:00Z",
        expires_at: "2026-08-09T10:00:00Z",
      })
    );
    vi.stubGlobal("fetch", fetchMock);

    await inviteMember(TENANT, "neu@firma.example", "member");

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API_BASE_URL}/companies/${TENANT}/invitations`);
    expect(Object.keys(JSON.parse(String(init.body))).sort()).toEqual(["email", "role"]);
  });

  it("names the refusal when a member tries to invite", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(403, { detail: "Only an administrator may invite" }))
    );

    const result = await inviteMember(TENANT, "neu@firma.example", "member");

    expect(result).toEqual({ ok: false, reason: "not-admin", message: expect.any(String) });
  });
});

describe("acceptInvitation", () => {
  it("sends the token and nothing else — the address comes from the server", async () => {
    const fetchMock = vi.fn(async () =>
      respond(200, { id: TENANT, name: "Firma", domain: "firma.example", role: "member" })
    );
    vi.stubGlobal("fetch", fetchMock);

    await acceptInvitation("abc123");

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({ token: "abc123" });
  });

  it("keeps 'expired' apart from 'invalid' — one is fixable, the other is not", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => respond(400, { detail: "This invitation has expired" }))
    );
    await expect(acceptInvitation("t")).resolves.toMatchObject({ reason: "refused" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(404, { detail: "not valid" })));
    await expect(acceptInvitation("t")).resolves.toMatchObject({ reason: "invalid" });
  });

  it("reports a lost session so the page can send you to the login", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(401, { detail: "not authenticated" })));

    await expect(acceptInvitation("t")).resolves.toMatchObject({ reason: "unauthenticated" });
  });
});

describe("withdrawInvitation", () => {
  it("accepts an empty 204 body without trying to parse it", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(204, null)));

    await expect(withdrawInvitation(TENANT, INVITATION)).resolves.toEqual({ ok: true });
  });
});

describe("listInvitations", () => {
  it("never receives a token — it is not in the contract", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        respond(200, [
          {
            id: INVITATION,
            email: "neu@firma.example",
            role: "member",
            status: "pending",
            created_at: "2026-08-02T10:00:00Z",
            expires_at: "2026-08-09T10:00:00Z",
          },
        ])
      )
    );

    const result = await listInvitations(TENANT);

    expect(result.ok).toBe(true);
    if (result.ok) expect(result.invitations[0]).not.toHaveProperty("token");
  });
});

describe("removeMember", () => {
  it("keeps 'last admin' apart from 'not allowed'", async () => {
    // Der eine Fall heißt "mach vorher jemanden zum Administrator", der andere
    // "such dir jemanden mit mehr Rechten". Eine gemeinsame Meldung ließe die
    // Person raten, was zu tun ist.
    vi.stubGlobal("fetch", vi.fn(async () => respond(409, { detail: "needs an admin" })));
    await expect(removeMember(TENANT, "u")).resolves.toMatchObject({ reason: "last-admin" });

    vi.stubGlobal("fetch", vi.fn(async () => respond(403, { detail: "only admins" })));
    await expect(removeMember(TENANT, "u")).resolves.toMatchObject({ reason: "not-admin" });
  });

  it("accepts the empty 204 body", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(204, null)));

    await expect(removeMember(TENANT, "u")).resolves.toEqual({ ok: true });
  });
});
