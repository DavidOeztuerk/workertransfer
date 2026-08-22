import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithProviders } from "../test/render";
import { useLogout, useSession } from "./session";

afterEach(() => vi.restoreAllMocks());

const PRINCIPAL = {
  user_id: "22222222-2222-2222-2222-222222222222",
  email: "a@b.com",
  tenant_id: "11111111-1111-1111-1111-111111111111",
};

function SessionProbe() {
  const { user, isLoading } = useSession();
  const logout = useLogout();
  if (isLoading) return <p>lädt</p>;
  return user === null ? (
    <p>anonym</p>
  ) : (
    <>
      <p>angemeldet: {user.tenant_id}</p>
      <button type="button" onClick={() => logout.mutate()}>
        Abmelden
      </button>
    </>
  );
}

/** Die Antwort von GET /auth/session — angemeldet. */
const AKTIV = { user: PRINCIPAL, state: "active" };
/** ... und für jemanden ohne jedes Cookie. */
const ANONYM = { user: null, state: "anonymous" };

describe("useSession", () => {
  it("reports the principal when the session is active", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify(AKTIV), { status: 200 }))
    );
    renderWithProviders(<SessionProbe />);
    expect(await screen.findByText(`angemeldet: ${PRINCIPAL.tenant_id}`)).toBeInTheDocument();
  });

  it("sends the cookie jar and no Authorization header", async () => {
    const calls: Array<[string, RequestInit | undefined]> = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string, init?: RequestInit) => {
        calls.push([url, init]);
        return new Response(JSON.stringify(AKTIV), { status: 200 });
      })
    );
    renderWithProviders(<SessionProbe />);
    await screen.findByText(/angemeldet/);

    const first = calls[0];
    expect(first).toBeDefined();
    const [url, init] = first!;
    // Die öffentliche Frage geht an /auth/session, nicht an das geschützte /me
    // — sonst wäre jeder Seitenaufruf eines Abgemeldeten ein 401.
    expect(url.endsWith("/auth/session")).toBe(true);
    expect(init?.credentials).toBe("include");
    // The token lives in an httpOnly cookie; the client must never try to
    // attach it as a header (it cannot read it in the first place).
    expect(init?.headers).toBeUndefined();
  });

  it("meldet anonym — und stellt dafür GENAU EINE Anfrage", async () => {
    // Der Grund für den ganzen Endpunkt: kein Fehlereintrag und kein zweiter
    // Weg für jemanden, der bloss liest.
    const wege: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        wege.push(url);
        return new Response(JSON.stringify(ANONYM), { status: 200 });
      })
    );
    renderWithProviders(<SessionProbe />);
    expect(await screen.findByText("anonym")).toBeInTheDocument();
    expect(wege).toHaveLength(1);
  });
});

describe("useLogout", () => {
  it("clears the cached session after POST /auth/logout", async () => {
    let authenticated = true;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (_url: string, init?: RequestInit) => {
        if (init?.method === "POST") {
          authenticated = false;
          return new Response("", { status: 204 });
        }
        return authenticated
          ? new Response(JSON.stringify(AKTIV), { status: 200 })
          : new Response(JSON.stringify(ANONYM), { status: 200 });
      })
    );

    renderWithProviders(<SessionProbe />);
    await screen.findByText(/angemeldet/);

    await userEvent.click(screen.getByRole("button", { name: "Abmelden" }));
    await waitFor(() => expect(screen.getByText("anonym")).toBeInTheDocument());
  });
});
