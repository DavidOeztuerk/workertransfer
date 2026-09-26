import { describe, expect, it } from "vitest";

import { ADMIN_CASES, failureKey, interpretError } from "./errors";

const failure = (status: number, detail: string) => ({ status, title: "Request failed", detail });

describe("failureKey", () => {
  it("never hands back the server's sentence — an unknown one falls to the step", () => {
    expect(failureKey(failure(500, "internal error"), "errors.stepFailed")).toBe("errors.stepFailed");
    expect(failureKey(failure(422, "Something new."), "errors.jobNotSaved")).toBe("errors.jobNotSaved");
  });

  it("reads no connection and an expired session by status", () => {
    expect(failureKey(failure(0, ""), "errors.stepFailed")).toBe("errors.noConnection");
    expect(failureKey(failure(401, "not authenticated"), "errors.stepFailed")).toBe(
      "errors.sessionExpiredLong",
    );
  });

  it("takes the key the status table chose", () => {
    const read = interpretError(
      failure(404, "No such transfer"),
      { 404: { reason: "gone", titleKey: "errors.notPossibleNow" } },
      "other",
    );
    // The server's words stay READABLE, the key is what is drawn.
    expect(read.error.detail).toBe("No such transfer");
    expect(failureKey(read.error, "errors.stepFailed")).toBe("errors.notPossibleNow");
  });

  it("lets the site's own case win over the table, by sentence", () => {
    const read = interpretError(
      failure(409, "the deadline for this assessment has passed"),
      { 409: { reason: "conflict", titleKey: "errors.stepNotPossible" } },
      "other",
    );
    expect(
      failureKey(read.error, "errors.notSubmitted", [
        { status: 409, pattern: /deadline .* has passed/, key: "assessments.deadlinePassed" },
      ]),
    ).toBe("assessments.deadlinePassed");
    expect(failureKey(read.error, "errors.notSubmitted")).toBe("errors.stepNotPossible");
  });

  it("tells the admin refusal from a missing company", () => {
    expect(failureKey(failure(403, "not permitted"), "errors.jobNotSaved", ADMIN_CASES)).toBe(
      "errors.adminOnly",
    );
    expect(failureKey(failure(403, "no active company"), "errors.jobNotSaved", ADMIN_CASES)).toBe(
      "errors.jobNotSaved",
    );
    expect(
      failureKey(failure(503, "the role lookup did not answer"), "errors.jobNotSaved", ADMIN_CASES),
    ).toBe("errors.serviceSilent");
  });
});
