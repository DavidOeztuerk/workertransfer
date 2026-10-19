import { describe, expect, it } from "vitest";

import {
  draftStepErrorKey,
  emptyLetterHintKey,
  writingKey,
  writingReason,
} from "./draftErrors";

describe("writingReason", () => {
  it("recognises that no provider is set up", () => {
    expect(writingReason("Es ist kein Entwurfsanbieter eingerichtet.")).toBe("noProvider");
    expect(writingReason("No writing-help provider is set up.")).toBe("noProvider");
    expect(writingReason("")).toBe("noProvider");
    expect(writingReason(null)).toBe("noProvider");
  });

  it("recognises a timeout without the raw sentence", () => {
    expect(writingReason("Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).")).toBe(
      "timeout",
    );
    expect(writingReason("The draft provider does not answer (timeout).")).toBe("timeout");
    expect(writingKey("Zeitueberschreitung")).toBe("drafts.timeout");
  });

  it("swallows HTTP status codes — 402, 404, 422 do not look like the page's error", () => {
    expect(writingReason("Der Entwurfsanbieter antwortet mit 404.")).toBe("providerRejected");
    expect(writingReason("Der Entwurfsanbieter antwortet mit 402.")).toBe("providerRejected");
    expect(writingReason("Der Entwurfsanbieter antwortet mit 422.")).toBe("providerRejected");
    expect(writingReason("The draft provider answers with 404.")).toBe("providerRejected");
    expect(writingKey("antwortet mit 404")).not.toContain("404");
  });

  it("recognises an empty or unusable answer in both languages", () => {
    expect(writingReason("The draft provider sent an unusable or empty response.")).toBe(
      "providerEmpty",
    );
    expect(writingReason("Der Entwurfsanbieter lieferte einen leeren Text.")).toBe(
      "providerEmpty",
    );
  });

  it("does not blame the provider when there was nothing to revise", () => {
    // `ApplicationDraft.cs`, /revise, 409 — it used to fall through to
    // providerNotWriting, and the person read that the writing help failed.
    expect(writingReason("There is no open comment — nothing to revise.")).toBe(
      "nothingToRevise",
    );
    expect(writingKey("There is no open comment — nothing to revise.")).toBe(
      "drafts.nothingToRevise",
    );
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

/*
 * The sentences below are applications-service's own, word for word
 * (`ApplicationDraft.cs`, `SendDraft.cs`, `JobApplication.cs`). Whoever rewords
 * one there makes the matching line here red instead of letting a German person
 * read the step's fallback where a specific sentence was meant.
 */
describe("draftStepErrorKey", () => {
  const failure = (status: number, detail: string) => ({ status, detail });

  it("chooses by status where the status says everything", () => {
    expect(draftStepErrorKey("approve", failure(0, "…"))).toBe("errors.noConnection");
    expect(draftStepErrorKey("send", failure(401, "authentication required"))).toBe(
      "errors.sessionExpiredLong",
    );
    expect(draftStepErrorKey("comment", failure(404, "no such draft"))).toBe(
      "errors.draftNotFound",
    );
  });

  it("names what the person has to do first on a conflict", () => {
    expect(
      draftStepErrorKey("approve", failure(409, "There are open comments — revise first.")),
    ).toBe("drafts.stepOpenComments");
    expect(draftStepErrorKey("send", failure(409, "Only an approved draft goes out."))).toBe(
      "drafts.stepApproveFirst",
    );
    expect(draftStepErrorKey("send", failure(409, "This job is no longer advertised."))).toBe(
      "errors.jobClosed",
    );
    expect(
      draftStepErrorKey("send", failure(409, "A Submitted application cannot become Submitted.")),
    ).toBe("drafts.applicationExists");
  });

  it("reads the German step sentence as “not from here”", () => {
    expect(
      draftStepErrorKey(
        "save",
        failure(409, "Ein Entwurf im Stand Sent kann nicht geändert werden."),
      ),
    ).toBe("drafts.stepNotNow");
  });

  it("names the rule a text broke", () => {
    expect(draftStepErrorKey("comment", failure(422, "A comment without text says nothing."))).toBe(
      "drafts.commentEmpty",
    );
    expect(
      draftStepErrorKey("comment", failure(422, "The comment is longer than 1000 characters.")),
    ).toBe("drafts.commentTooLong");
    expect(
      draftStepErrorKey("comment", failure(422, "A draft carries no more than 50 comments.")),
    ).toBe("drafts.tooManyComments");
    expect(draftStepErrorKey("approve", failure(422, "Nobody approves an empty cover letter."))).toBe(
      "drafts.approveEmpty",
    );
    expect(
      draftStepErrorKey("save", failure(422, "The cover letter is longer than 20000 characters.")),
    ).toBe("drafts.letterTooLong");
    expect(
      draftStepErrorKey("send", failure(422, "The message is longer than 4000 characters.")),
    ).toBe("drafts.letterTooLong");
  });

  it("falls back to the step's own sentence, never to the server's", () => {
    expect(draftStepErrorKey("save", failure(500, "internal error"))).toBe("errors.draftNotSaved");
    expect(draftStepErrorKey("attachments", failure(422, "Something new."))).toBe(
      "errors.draftNotSaved",
    );
    expect(draftStepErrorKey("approve", failure(500, "internal error"))).toBe(
      "errors.draftStepFailed",
    );
    expect(draftStepErrorKey("delete", failure(500, "internal error"))).toBe(
      "errors.draftNotDeleted",
    );
  });
});
