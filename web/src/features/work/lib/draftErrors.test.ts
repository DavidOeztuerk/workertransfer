import { describe, expect, it } from "vitest";

import { emptyLetterHintKey, writingKey, writingReason } from "./draftErrors";

describe("writingReason", () => {
  it("recognises that no provider is set up", () => {
    expect(writingReason("Es ist kein Entwurfsanbieter eingerichtet.")).toBe("noProvider");
    expect(writingReason("")).toBe("noProvider");
    expect(writingReason(null)).toBe("noProvider");
  });

  it("recognises a timeout without the raw sentence", () => {
    expect(writingReason("Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).")).toBe(
      "timeout",
    );
    expect(writingKey("Zeitueberschreitung")).toBe("drafts.timeout");
  });

  it("swallows HTTP status codes — 402, 404, 422 do not look like the page's error", () => {
    expect(writingReason("Der Entwurfsanbieter antwortet mit 404.")).toBe("providerRejected");
    expect(writingReason("Der Entwurfsanbieter antwortet mit 402.")).toBe("providerRejected");
    expect(writingReason("Der Entwurfsanbieter antwortet mit 422.")).toBe("providerRejected");
    expect(writingKey("antwortet mit 404")).not.toContain("404");
  });

  it("does not attribute a forbidden step to the provider", () => {
    expect(
      writingReason("Ein Entwurf im Stand Fehlgeschlagen kann nicht geschrieben werden."),
    ).toBe("stepNotNow");
  });

  it("chooses the empty hint by the reason, not always “no provider”", () => {
    expect(emptyLetterHintKey("Es ist kein Entwurfsanbieter eingerichtet.")).toBe(
      "drafts.emptyLetterHint",
    );
    expect(emptyLetterHintKey("Zeitüberschreitung")).toBe("drafts.timeout");
    expect(emptyLetterHintKey("antwortet mit 404")).toBe("drafts.providerRejected");
  });
});
