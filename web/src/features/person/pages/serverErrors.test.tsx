import userEvent from "@testing-library/user-event";
import { cleanup, screen } from "@testing-library/react";
import type { ReactElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { SIGNED_IN, renderWithStore } from "../test/render";
import { ConsentsPage } from "./ConsentsPage";
import { DeleteAccountPage } from "./DeleteAccountPage";
import { GitHubPage } from "./GitHubPage";
import { PortfolioPage } from "./PortfolioPage";
import { ProfilePage } from "./ProfilePage";
import { ResumePage } from "./ResumePage";

/**
 * A GERMAN PERSON READS GERMAN when a service refuses — on her own pages too.
 *
 * Every service answers in English, and until 26.09.2026 these pages drew the
 * problem document's `detail` (and, through `ErrorBlock`, its `title`) raw.
 * Each case feeds the service's own words and requires the catalogue's German
 * sentence — and that the English stands nowhere on the page.
 */

function failEverything(status: number, detail: string) {
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async () =>
        new Response(JSON.stringify({ title: "Request failed", detail, correlationId: "c-1" }), {
          status,
          headers: { "content-type": "application/json" },
        }),
    ),
  );
}

function noEnglish() {
  expect(document.body.textContent ?? "").not.toMatch(/internal error|Request failed/);
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("a failed load reads in German", () => {
  const cases: [string, () => ReactElement, string][] = [
    ["the résumé requests", () => <ResumePage />, "Die Anfragen sind gerade nicht abrufbar."],
    ["the profile", () => <ProfilePage />, "Dein Profil liess sich nicht laden."],
    ["the portfolio", () => <PortfolioPage />, "Die Arbeiten sind gerade nicht abrufbar."],
    ["the GitHub connection", () => <GitHubPage />, "Die Verbindung ließ sich nicht laden."],
    ["the releases", () => <ConsentsPage />, "Deine Freigaben liessen sich nicht laden."],
  ];

  it.each(cases)("on %s", async (_name, page, german) => {
    failEverything(500, "internal error");
    renderWithStore(page(), { auth: SIGNED_IN });

    expect((await screen.findAllByText(german)).length).toBeGreaterThan(0);
    noEnglish();
  });
});

describe("a refused step reads in German", () => {
  it("an erasure the service does not take", async () => {
    failEverything(500, "internal error");
    renderWithStore(<DeleteAccountPage />, { auth: SIGNED_IN });

    await userEvent.click(await screen.findByRole("button", { name: "Konto löschen" }));
    await userEvent.click(await screen.findByRole("button", { name: "Ja, endgültig löschen" }));

    expect(
      await screen.findByText("Die Löschung konnte nicht angenommen werden."),
    ).toBeInTheDocument();
    noEnglish();
  });
});
