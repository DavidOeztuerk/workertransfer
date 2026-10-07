import { afterEach, describe, expect, it, vi } from "vitest";

import { createJob, salaryText } from "./jobs";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("createJob", () => {
  /**
   * Until 07.10.2026 the form's `remote` and `employment` went out under those
   * names, the service reads `remote_mode` and `employment_type`, and every
   * advert became "on site, full time". The body is read here with the
   * SERVICE's words, not the UI's.
   */
  it("sends what the form says under the service's names", async () => {
    let sent: Record<string, unknown> = {};
    vi.stubGlobal(
      "fetch",
      vi.fn(async (_input: unknown, init?: RequestInit) => {
        sent = JSON.parse(String(init?.body));
        return new Response(JSON.stringify({ id: "j", skills: [] }), {
          status: 201,
          headers: { "content-type": "application/json" },
        });
      }),
    );

    await createJob({
      title: "Welder",
      description: "TIG",
      location: "Kiel",
      postal_code: "24103",
      remote: "hybrid",
      employment: "part_time",
      skills: ["WIG"],
      salary: { min: 3000, max: 3400, period: "month" },
    });

    expect(sent).toMatchObject({
      remote_mode: "hybrid",
      employment_type: "part_time",
      postal_code: "24103",
      salary: { min: 3000, max: 3400, period: "month" },
    });
    expect(sent).not.toHaveProperty("remote");
    expect(sent).not.toHaveProperty("employment");
  });
});

describe("salaryText", () => {
  it("reads as a range with its period", () => {
    expect(
      salaryText({ min: 42000, max: 52000, period: "year", currency: "EUR" }, "de", () => "brutto/Jahr"),
    ).toMatch(/42\.000\s€ – 52\.000\s€ brutto\/Jahr/);
  });

  it("names a single figure once", () => {
    expect(
      salaryText({ min: 18, max: 18, period: "hour", currency: "EUR" }, "de", () => "brutto/Stunde"),
    ).toMatch(/^18\s€ brutto\/Stunde$/);
  });
});
