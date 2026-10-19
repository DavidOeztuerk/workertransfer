// The work sample through the real stack — at the three places where it
// differs from everything else (ADR-0042).
//
// FIRST: the EFFORT comes first. This journey reads "4 Stunden" (4 hours) on the
// person's page and refuses a task of nine hours — a limit that stands only in
// the server is one nobody sees.
//
// SECOND: the PERSON sees the evaluation, even on a rejection. The path
// therefore deliberately goes through `rejected` and not `accepted`: with an
// acceptance nobody would notice if the feedback were missing.
//
// THIRD: there is no decline button, and the page says so. This journey checks
// both — that the button is missing and that the sentence is there — because a
// missing button without a sentence looks like an oversight.
//
// The wire is the reason this journey has to exist: `due_at` bound as `dueAt`
// would never arrive, and every handler and component suite would stay green.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  pickOption,
} from "./stack";

skipWithoutStack();

/** A date in the future, as `YYYY-MM-DD` for a date field. */
function inDays(days: number): string {
  const target = new Date(Date.now() + days * 24 * 60 * 60 * 1000);

  return target.toISOString().slice(0, 10);
}

test("effort up front, rejection with a reason, no decline button", async ({ browser }) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("assessment.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Assessment ${stamp}`;
  const headline = `E2E Assessment candidate ${stamp}`;

  // --- The person: profile and release ------------------------------------

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Assessee");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // A work sample stands on the same release as a conversation — on an EXISTING
  // visibility, not on a capability of its own.
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // One's own id — the same one a company gets from the scout or a
  // conversation. Via identity-service's port and not via the Vite server: a
  // relative address would hit the dev server, and that does not know `/me`.
  const subjectId = await candidate.evaluate(async () => {
    const response = await fetch(
      `${window.location.origin.replace(":5173", ":8001")}/me`,
      { credentials: "include" }
    );
    return ((await response.json()) as { user_id: string }).user_id;
  });

  expect(subjectId).not.toBe("");

  // --- The company sets a task --------------------------------------------

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/company/assessments");

  // THE COUNTER-PROBE FIRST: nine hours do not go. More than one working day is
  // no longer a sample but work — and the page says so instead of letting
  // somebody try it out.
  await expect(recruiter.getByText(/Höchstens 8/)).toBeVisible();

  await recruiter.getByLabel("Wem").fill(subjectId);
  await recruiter.getByLabel("Überschrift").fill(`Small service ${stamp}`);
  await recruiter
    .getByLabel("Die Aufgabe")
    .fill("Build a small service that serves a list.");
  await recruiter.getByLabel(/Umfang in Stunden/i).fill("9");
  await recruiter.getByLabel(/Bis wann/i).fill(inDays(7));
  await recruiter.getByRole("button", { name: "Aufgabe stellen", exact: true }).click();

  // The server names the rule ("the stated effort is between 1 and 8 hours",
  // English on the wire); the page says it in the person's language. Until
  // 26.09.2026 this line expected the server's English sentence, i.e. it
  // pinned that the page drew it raw.
  await expect(recruiter.getByText(/Der Umfang liegt außerhalb des Erlaubten/)).toBeVisible();
  await expect(recruiter.getByText(/between 1 and 8 hours/i)).toHaveCount(0);

  // And now properly.
  await recruiter.getByLabel(/Umfang in Stunden/i).fill("4");
  await recruiter.getByRole("button", { name: "Aufgabe stellen", exact: true }).click();

  const companyCard = recruiter.locator(".MuiCard-root").filter({
    hasText: `Small service ${stamp}`,
  });
  await expect(companyCard).toBeVisible();

  // --- The person sees the effort, and no decline button ------------------

  await candidate.goto("/assessments");

  const card = candidate.locator(".MuiCard-root").filter({
    hasText: `Small service ${stamp}`,
  });
  await expect(card).toBeVisible();

  // THE EFFORT, on the page and not in the small print.
  await expect(card.getByText("4 Stunden")).toBeVisible();

  // NO DECLINE BUTTON — and the sentence that says this is intended.
  await expect(candidate.getByText(/es gibt keinen Absageknopf/i)).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Ablehnen|Absagen/i })).toHaveCount(0);

  // --- Submit -------------------------------------------------------------

  await card.getByLabel("Deine Lösung").fill("It is in the repository.");
  await card.getByLabel(/^Link/).fill("https://example.test/solution");
  await card.getByRole("button", { name: "Abgeben", exact: true }).click();

  // `exact`, because "Abgegeben" would otherwise also match the heading "Was du
  // abgegeben hast" — two matches are an error in Playwright, and a loose
  // selector here once brought fourteen journeys to a halt.
  await expect(card.getByText("Abgegeben", { exact: true })).toBeVisible();

  // --- A REJECTION, and it needs a text -----------------------------------

  await recruiter.reload();
  await expect(companyCard.getByText("https://example.test/solution")).toBeVisible();

  await companyCard.getByRole("radio", { name: /Es geht nicht weiter/ }).check();

  // THE PROMISE: without a text the button is disabled. Rejecting and giving a
  // reason are one step, not two.
  await expect(companyCard.getByRole("button", { name: /Rückmeldung senden/i })).toBeDisabled();

  await companyCard
    .getByLabel("Deine Rückmeldung")
    .fill("Cleanly solved, but the error handling is missing.");
  await companyCard.getByRole("button", { name: /Rückmeldung senden/i }).click();

  // --- And the person reads it — although it was a rejection -------------

  await candidate.goto("/assessments");
  await expect(card.getByText("Es geht nicht weiter.")).toBeVisible();
  await expect(
    card.getByText(/Cleanly solved, but the error handling is missing\./)
  ).toBeVisible();

  // --- She reads it after a withdrawal too --------------------------------
  //
  // THE SHARPEST CASE: whoever takes back their visibility disappears for the
  // company — but not from their own evaluation. A judgement one can be locked
  // out of would be one the judged never reads.

  await candidate.goto("/profile");
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await candidate.goto("/assessments");
  await expect(
    card.getByText(/Cleanly solved, but the error handling is missing\./)
  ).toBeVisible();

  await recruiter.reload();
  await expect(recruiter.getByText(`Small service ${stamp}`)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
