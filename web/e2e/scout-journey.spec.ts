// The scout through the real stack — at exactly the point that sets it apart
// from its predecessor.
//
// `GET /candidates` could search and filter; what it could not do was say WHAT
// somebody is missing. It searched with AND — every hit satisfied everything,
// every tick would have been set, and the list would have said nothing. This
// journey drives exactly the difference: two searched words, a person who
// named one of them — and a tick that names it.
//
// And it drives the conditions one otherwise only reads in the source: no
// number on the card, and for somebody without evidence a SENTENCE instead of
// an empty box (ADR-0022 §3).

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

test("the tick says WHICH skill is missing — and names no number", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Scout ${Date.now()}`;
  const stamp = Date.now();

  // Two words, both invented, so that this journey sees nothing of the others.
  const named = `Welding${stamp}`;
  const missing = `Machining${stamp}`;
  const headline = `E2E Scout candidate ${stamp}`;

  // The person names ONE of the two words and releases her profile.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Fähigkeiten/i).fill(named);
  await candidate.getByLabel("Standort", { exact: true }).fill("Berlin");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // The search is for BOTH words. Under AND nobody would come back; under OR
  // this person comes — and the ticks say why.
  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(`${named}, ${missing}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();

  // THE POINT OF THE WHOLE JOURNEY: the named word with a tick, the missing one
  // without.
  await expect(card.getByText(`${named} ✓`)).toBeVisible();
  await expect(card.getByText(`${missing} ✗`)).toBeVisible();

  // And no number beside it. Not "1 von 2" (1 of 2), not "50 %", nothing.
  await expect(card.getByText(/1 von 2|50\s*%|Passung|Score/i)).toHaveCount(0);

  // Whoever has nothing on GitHub is not worse but elsewhere — and the card
  // says so instead of showing an empty box (ADR-0022 §3).
  await expect(card.getByText(/Keine Belege freigegeben/)).toBeVisible();

  // The replaced path leads nowhere any more: the route is gone, and the UI
  // shows its own "not found" page instead of the list.
  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("whoever lives further away stays in the list — with a cross", async ({
  browser,
}) => {
  // ADR-0041 through the whole stack. The point is a statement about the LENGTH
  // of the list: the job does not filter, it explains. Whoever lies outside
  // stays in and carries a cross — the company decides, not the search, and it
  // sees why.
  test.slow();

  const stamp = Date.now();
  const skill = `ArmatureWinding${stamp}`;
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Commute ${stamp}`;

  const nearHeadline = `E2E Near ${stamp}`;
  const farHeadline = `E2E Far ${stamp}`;

  // Two people with the same skill, both released — and two different
  // statements about their commute. The places are real towns for the place
  // table ("München", not "Munich"); "Bis 50 km"/"Bis 10 km" are the option
  // labels.
  for (const [location, commute, headline] of [
    ["Potsdam", "Bis 50 km", nearHeadline],
    ["München", "Bis 10 km", farHeadline],
  ] as const) {
    const context = await browser.newContext();
    const personPage = await context.newPage();
    const email = uniqueEmail("candidate.example");
    await registerAndConfirm(personPage, email, "E2E Candidate");
    await login(personPage, email);
    await personPage.goto("/profile");
    await personPage.getByLabel(/Überschrift/i).fill(headline);
    await personPage.getByLabel(/Fähigkeiten/i).fill(skill);
    await personPage.getByLabel("Standort", { exact: true }).fill(location);
    await pickOption(personPage, /Wie weit würdest du pendeln/i, commute);
    await personPage.getByRole("button", { name: "Speichern", exact: true }).click();
    await expect(personPage.getByText(/Profil gespeichert/i)).toBeVisible();
    await personPage.getByRole("switch").click();
    await expect(personPage.getByRole("switch")).toBeChecked();
    await context.close();
  }

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // A job in Berlin, on site.
  await recruiter.goto("/company/jobs/new");
  const title = `E2E Job Berlin ${stamp}`;
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("On site in Berlin.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Berlin");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  await expect(recruiter.getByText(title)).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(skill);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  // Without a job: both there, no tick for reachability.
  await expect(recruiter.locator("li").filter({ hasText: nearHeadline })).toBeVisible();
  await expect(recruiter.locator("li").filter({ hasText: farHeadline })).toBeVisible();

  // With a job: BOTH still there — one with a tick, one with a cross.
  await pickOption(recruiter, /Gegen welche Stelle/i, title);

  const near = recruiter.locator("li").filter({ hasText: nearHeadline });
  const far = recruiter.locator("li").filter({ hasText: farHeadline });

  await expect(near).toBeVisible();
  await expect(far).toBeVisible();
  await expect(near.getByText("Erreichbarkeit ✓")).toBeVisible();
  await expect(far.getByText("Erreichbarkeit ✗")).toBeVisible();

  // And beside them stand the STATEMENTS, not the distance.
  await expect(far.getByText(/sagt: Bis 10 km/)).toBeVisible();
  await expect(far.getByText(/\d+\s*km entfernt|505/)).toHaveCount(0);

  await recruiterContext.close();
});
