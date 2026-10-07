import { afterEach, describe, expect, it, vi } from "vitest";

import { failureKey } from "../../../shared/api/errors";
import { advertCandidates } from "./scout";

afterEach(() => vi.unstubAllGlobals());

function answer(status: number, body: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })),
  );
}

describe("advertCandidates", () => {
  it("asks the advert's own address", async () => {
    answer(200, { items: [], next: null });
    await advertCandidates("j1");
    const url = String((fetch as unknown as { mock: { calls: unknown[][] } }).mock.calls[0]![0]);
    expect(url).toMatch(/\/scout\/jobs\/j1\/candidates$/);
  });

  it("names an advert without skills, not a generic failure", async () => {
    answer(422, { title: "Request failed", detail: "the advert names no skill — there is nothing to match" });
    const result = await advertCandidates("j1");
    expect(result.ok).toBe(false);
    if (!result.ok) expect(failureKey(result.error, "errors.searchFailed")).toBe("errors.advertNoSkills");
  });

  it("names a gone advert", async () => {
    answer(404, { title: "Request failed", detail: "No such advert" });
    const result = await advertCandidates("j1");
    if (!result.ok) expect(failureKey(result.error, "errors.searchFailed")).toBe("errors.advertGone");
  });
});
