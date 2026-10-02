import userEvent from "@testing-library/user-event";
import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { MarketPage } from "./MarketPage";

/**
 * The form shows the STORED status, or none at all.
 *
 * Until 02.10.2026 it drew its defaults ("not right now") at once and adopted
 * the stored status whenever the answer arrived. Whoever chose faster than the
 * answer came had the choice overwritten in silence, and "Speichern" then wrote
 * "not right now" for a person who had just clicked "Ich höre zu". Measured in
 * the transfer journey under load: the company's card read "Gerade nicht
 * ansprechbar".
 */

const PERSON = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null,
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const STORED = {
  subject_id: PERSON.session.userId,
  availability: "listening",
  employed: true,
  note: "",
  is_approachable: false,
  updated_at: "2026-09-01T00:00:00Z",
};

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });

/** `/market/me` answers when the test says so; everything else at once. */
function stubFetch(status: () => Promise<Response>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url.includes("/market/me/requests")) return json(200, []);
      if (url.endsWith("/market/me")) return status();
      return json(200, null);
    }),
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the market status form", () => {
  it("offers no choice before the stored status is known, then shows it", async () => {
    let answer: (response: Response) => void = () => undefined;
    stubFetch(() => new Promise((resolve) => (answer = resolve)));
    renderWithStore(<MarketPage />, { auth: PERSON });

    // Nothing to click while the answer is out: a choice made now would be
    // overwritten when it comes.
    expect(await screen.findByText("Marktstatus wird geladen…")).toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Speichern$/ })).not.toBeInTheDocument();

    answer(json(200, STORED));

    expect(await screen.findByRole("radio", { name: /Ich höre zu/ })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: /Ich arbeite gerade irgendwo/ })).toBeChecked();
  });

  it("keeps a choice made after loading", async () => {
    stubFetch(async () => json(200, { ...STORED, availability: "unavailable" }));
    renderWithStore(<MarketPage />, { auth: PERSON });

    const listening = await screen.findByRole("radio", { name: /Ich höre zu/ });
    await userEvent.click(listening);

    expect(listening).toBeChecked();
  });

  it("offers no form when the stored status cannot be read", async () => {
    stubFetch(async () =>
      json(503, { title: "Request failed", detail: "the consent ledger did not answer" }),
    );
    renderWithStore(<MarketPage />, { auth: PERSON });

    // Saving blind would write the defaults over whatever is stored.
    expect(
      await screen.findByText(/Ändern lässt er sich erst wieder, wenn er lesbar ist/),
    ).toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Speichern$/ })).not.toBeInTheDocument();
  });
});
