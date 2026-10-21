import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ALL_ON } from "../api/notificationPreferences";
import { PERSON, renderWithStore } from "../test/render";
import { SettingsPage } from "./SettingsPage";

/**
 * What a person without a company is shown — and what she is not: a switch
 * for applications arriving at "your company", and a note about company
 * records, both mean nothing without a membership.
 */
function stubFetch() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      const body = url.includes("/me/notification-preferences") ? ALL_ON : null;
      return new Response(JSON.stringify(body), {
        status: body === null ? 404 : 200,
        headers: { "content-type": "application/json" },
      });
    }),
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("SettingsPage", () => {
  it("offers a person without a company no company switch", async () => {
    stubFetch();
    renderWithStore(<SettingsPage />, { auth: { status: "authenticated", session: PERSON } });

    expect(await screen.findByText(/Lebenslauf fragt/)).toBeInTheDocument();
    expect(screen.queryByText(/Bewerbung bei deinem Unternehmen eingeht/)).not.toBeInTheDocument();
    expect(screen.queryByText("Für dein Unternehmen.")).not.toBeInTheDocument();
  });

  it("offers it to a member", async () => {
    stubFetch();
    renderWithStore(<SettingsPage />, {
      auth: {
        status: "authenticated",
        session: PERSON,
        memberships: [{ id: "22222222-2222-4222-8222-222222222222", name: "Muster GmbH", role: "member" }],
      },
    });

    expect(await screen.findByText(/Bewerbung bei deinem Unternehmen eingeht/)).toBeInTheDocument();
  });
});
