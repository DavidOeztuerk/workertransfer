import { cleanup, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../../../features/auth/test/render";
import { CompanySwitcher } from "./CompanySwitcher";

/**
 * Back to oneself without signing out: "Ich selbst" is a choice like the
 * companies, and it asks the server for a person token.
 */
const TENANT = "22222222-2222-4222-8222-222222222222";
const SESSION = {
  user_id: "11111111-1111-1111-1111-111111111111",
  email: "anna@example.com",
  tenant_id: null,
  language: "de",
  display_name: "Anna Beispiel",
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("CompanySwitcher", () => {
  it("goes back to oneself through POST /auth/self", async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();
        calls.push(`${init?.method ?? "GET"} ${new URL(url).pathname}`);
        const body = url.endsWith("/auth/session") ? { user: SESSION } : { status: "ok" };
        return new Response(JSON.stringify(body), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      }),
    );
    const user = userEvent.setup();
    const { store } = renderWithStore(<CompanySwitcher fullWidth />, {
      auth: {
        status: "authenticated",
        session: {
          userId: SESSION.user_id,
          email: SESSION.email,
          tenantId: TENANT,
          language: "de",
          displayName: "Anna Beispiel",
          givenName: "",
          familyName: "",
          occupationalField: null,
        },
        memberships: [{ id: TENANT, name: "Muster GmbH", role: "admin" }],
      },
    });

    await user.click(screen.getByRole("combobox", { name: "Handeln als" }));
    await user.click(screen.getByRole("option", { name: "Ich selbst" }));

    await vi.waitFor(() => expect(store.getState().auth.session?.tenantId).toBeNull());
    expect(calls).toContain("POST /auth/self");
  });
});
