import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { renderWithStore } from "../test/render";
import { CompanyProfilePage } from "./CompanyProfilePage";

/**
 * The company's shop window: the form shows the STORED profile, or none.
 *
 * Drawn empty while the answer was out, a name typed in that gap was replaced
 * when the answer came; and with no answer at all, "Speichern" wrote empty
 * fields over the profile every applicant sees.
 */

const FOR_COMPANY = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@firma.de",
    tenantId: "22222222-2222-4222-8222-222222222222",
    language: "de",
    displayName: "Anna Beispiel",
    givenName: "",
    familyName: "",
    occupationalField: null,
  },
};

const STORED = {
  display_name: "Acme Schweißtechnik",
  about: "Wir schweißen.",
  website: null,
  locations: ["Leipzig"],
  benefits: [],
  line1: null,
  postal_code: null,
  city: null,
  country: "DE",
  phone: null,
};

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });

function stubFetch(profile: () => Promise<Response>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === "string" ? input : input.toString();
      return url.includes("/companies/me/profile") ? profile() : json(200, null);
    }),
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the company profile form", () => {
  it("offers no field before the stored profile is known, then shows it", async () => {
    let answer: (response: Response) => void = () => undefined;
    stubFetch(() => new Promise((resolve) => (answer = resolve)));
    renderWithStore(<CompanyProfilePage />, { auth: FOR_COMPANY });

    expect(await screen.findByText("Profil wird geladen…")).toBeInTheDocument();
    expect(screen.queryByLabelText(/Anzeigename/)).not.toBeInTheDocument();

    answer(json(200, STORED));

    expect(await screen.findByLabelText(/Anzeigename/)).toHaveValue("Acme Schweißtechnik");
  });

  it("offers an empty form when there is no profile yet", async () => {
    stubFetch(async () => json(200, null));
    renderWithStore(<CompanyProfilePage />, { auth: FOR_COMPANY });

    expect(await screen.findByLabelText(/Anzeigename/)).toHaveValue("");
  });

  it("offers no form when the stored profile cannot be read", async () => {
    stubFetch(async () => json(503, { title: "Request failed", detail: "unavailable" }));
    renderWithStore(<CompanyProfilePage />, { auth: FOR_COMPANY });

    expect(await screen.findByText("Das Profil ist gerade nicht abrufbar.")).toBeInTheDocument();
    expect(screen.queryByLabelText(/Anzeigename/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Speichern" })).not.toBeInTheDocument();
  });
});
