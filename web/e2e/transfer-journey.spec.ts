// The transfer market through the browser, from the market status to the
// completion.
//
// The path from the definition of done, once in full: set the status → request
// → release → interest → conversation → offer → acceptance → confirm the
// release → completion. Two browser contexts, because there are two sides and
// each may only have its own buttons.

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

test("a transfer comes only from three yeses — and the employer is never asked", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Transfer market ${Date.now()}`;
  const headline = `E2E Transfer candidate ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  // Create the profile and release it — without that nobody may ask for the
  // market status, otherwise the request would be a channel for learning it
  // exists.
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // "Ich höre zu" (listening) AND "ich arbeite gerade irgendwo" (currently
  // employed) — the normal case on a transfer market, and the path on which a
  // release becomes necessary.
  await candidate.goto("/market");
  await candidate.getByRole("radio", { name: /Ich höre zu/ }).check();
  await candidate.getByRole("checkbox", { name: /Ich arbeite gerade irgendwo/ }).check();
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Marktstatus gespeichert/i)).toBeVisible();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  // Wait until the switch really holds: the choice only sets it off.
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout.
  await expect(card).toBeVisible();
  // The market status is a release of its own, separate from the resume.
  // Before it, there is nothing to see here and nothing to offer.
  await expect(card.getByRole("button", { name: /Interesse zeigen/i })).toHaveCount(0);
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  // The start page tells the person that something is pending — without them
  // looking for it. Assembled in the browser from four services.
  await candidate.goto("/");
  await expect(
    candidate.getByText(/Unternehmen möchte(n)? sehen, ob du ansprechbar bist/)
  ).toBeVisible();

  // The person decides.
  await candidate.goto("/market");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Answered means: disappears from "Was liegt an" (what is pending).
  await candidate.goto("/");
  await expect(
    candidate.getByText(/Unternehmen möchte(n)? sehen, ob du ansprechbar bist/)
  ).toHaveCount(0);

  // Only now does the company see the status — and may approach.
  await recruiter.goto("/scout");
  const openCard = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(openCard).toBeVisible();
  await expect(openCard.getByText(/Hört zu/)).toBeVisible();
  await openCard.getByRole("button", { name: /Interesse zeigen/i }).click();
  await expect(openCard.getByText(/Interesse hinterlegt/i)).toBeVisible();

  // The person accepts the conversation.
  await candidate.goto("/transfers");
  await expect(
    candidate.getByRole("heading", { name: /Ein Unternehmen hat Interesse/ })
  ).toBeVisible();
  await candidate.getByRole("button", { name: /Gespräch annehmen/i }).click();
  await expect(candidate.getByRole("heading", { name: /Ihr seid im Gespräch/ })).toBeVisible();

  // The company makes an offer — text and start, no fee: a fee is agreed
  // between two companies, never offered to the person (ADR-0050).
  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByText(/Braucht eine Freigabe/)).toBeVisible();
  await expect(recruiter.getByLabel(/Ablöse/)).toHaveCount(0);
  await recruiter.getByLabel(/^Angebot/).fill("Backend team lead");
  await recruiter.getByLabel(/^Start/).fill("2026-11");
  await recruiter.getByRole("button", { name: /Angebot machen/i }).click();
  await expect(recruiter.getByRole("heading", { name: /Angebot abgegeben/ })).toBeVisible();
  // She opened no route to her employer: nothing about one appears.
  await expect(recruiter.getByRole("region", { name: "Frühere Freigabe beim Arbeitgeber" })).toHaveCount(0);

  // The person accepts — and afterwards has to confirm herself that her
  // employer lets her go. Nobody asked him.
  await candidate.goto("/transfers");
  await candidate.getByRole("button", { name: /Angebot annehmen/i }).click();
  await expect(candidate.getByText(/Es fehlt noch, dass dein Arbeitgeber dich gehen lässt/)).toBeVisible();

  // The company cannot complete here — never. If the case needs a release, the
  // last step belongs to the person: she is the only one who knows whether she
  // may go.
  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByRole("button", { name: /^Abschließen$/ })).toHaveCount(0);

  // Her confirmation completes the transfer.
  await candidate.getByRole("button", { name: /Freigabe bestätigen und abschließen/i }).click();
  await expect(candidate.getByRole("heading", { name: /^Abgeschlossen$/ })).toBeVisible();

  await recruiter.goto("/company/transfers");
  await expect(recruiter.getByRole("heading", { name: /^Abgeschlossen$/ })).toBeVisible();

  // From a final outcome there is no way back.
  await expect(recruiter.getByRole("button", { name: /Zurückziehen/i })).toHaveCount(0);
  await candidate.goto("/transfers");
  await expect(candidate.getByRole("button", { name: /Ablehnen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("without a market status release there is nothing to see and nothing to do", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Declined ${Date.now()}`;
  const headline = `E2E Declining candidate ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  await candidate.goto("/market");
  await candidate.getByRole("radio", { name: /Ich suche aktiv/ }).check();
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Marktstatus gespeichert/i)).toBeVisible();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  // The person declines. "Sucht aktiv" (actively looking) thus stays invisible
  // — and that she is looking is the most delicate item in the whole system.
  await candidate.goto("/market");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Ablehnen/i }).click();
  await expect(row.getByText(/Abgelehnt/i)).toBeVisible();

  await recruiter.goto("/scout");
  const shut = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(shut).toBeVisible();
  await expect(shut.getByText(/Marktstatus abgelehnt/)).toBeVisible();
  await expect(shut.getByText(/Sucht aktiv/)).toHaveCount(0);
  await expect(shut.getByRole("button", { name: /Interesse zeigen/i })).toHaveCount(0);
  // And asking again is not possible: a no that can be bypassed is none.
  await expect(shut.getByRole("button", { name: /Marktstatus anfragen/i })).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
