// The notification through the real stack — and what it does NOT say.
//
// The integration test proves the content on the in-process sending path. Here
// it runs over SMTP to Mailpit, as in production: identity-service writes,
// transfer-service triggered it, and in between lies real HTTP with the shared
// secret. A missing `WORKERTRANSFER_NOTIFY_SECRET` in one of the two containers
// would be visible on no other level.

import { expect, test } from "@playwright/test";

import {
  lastMailFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  pickOption,
} from "./stack";

skipWithoutStack();

test("a request reaches the person by mail — and the mail does not reveal what it is about", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Notifier ${Date.now()}`;
  const headline = `E2E Notified candidate ${Date.now()}`;

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

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // From here on only what is delivered after this moment counts — the
  // confirmation mail from before would otherwise pass as a hit.
  const since = Date.now();

  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  const mail = await lastMailFor(candidateEmail, { after: since });
  expect(mail).not.toBeNull();

  const text = `${mail?.subject ?? ""}\n${mail?.text ?? ""}`
    .toLowerCase()
    // Drop the brand name: "WorkerTransfer" contains "transfer".
    .replace(/workertransfer/g, "");
  // The words are German because the mail is: identity-service writes in the
  // account's language, and this account was registered under `de-DE`.
  for (const leak of [
    "marktstatus",
    "lebenslauf",
    "bewerbung",
    "transfer",
    "anfrage",
    "market_request",
    companyName.toLowerCase(),
    headline.toLowerCase(),
  ]) {
    expect(text).not.toContain(leak);
  }
  // But a way back is in it — otherwise the mail would be truly useless.
  expect(mail?.text ?? "").toContain("http");

  await candidateContext.close();
  await recruiterContext.close();
});

test("whoever unsubscribes from the kind gets no more mail about it", async ({ browser }) => {
  // Triple budget, because of the SHAPE of this journey, not the machine: it is
  // the only one that deliberately spends a full mail window (60 s) waiting for
  // nothing — an absence can only be proved by letting time pass. On top come
  // two registrations with a mail round trip, creating a company and a switch.
  // 150 s are not enough for that, and shortening the window would be no repair
  // but a weaker statement.
  test.slow();
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Muted ${Date.now()}`;
  const headline = `E2E Muted candidate ${Date.now()}`;

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

  // Unsubscribe — the switch takes effect immediately, there is no save button.
  await candidate.goto("/settings");
  const marketSwitch = candidate.getByRole("switch", { name: /Marktstatus sehen möchte/ });
  await expect(marketSwitch).toBeChecked();

  // Wait for the RESPONSE, not for the switch. The switch flips at once — the
  // page writes down the new position before the server has it — and the
  // `goto` below aborts a request still in flight. Then the tick is back after
  // the reload, and the journey falls at a place that has nothing to do with
  // its statement. Measured: exactly that, once in a run of 25.
  //
  // No higher time limit: that would look like a repair and only move the race.
  const saved = candidate.waitForResponse(
    (response) =>
      response.url().includes("/me/notification-preferences") &&
      response.request().method() === "PUT",
  );
  await marketSwitch.click();
  await expect(marketSwitch).not.toBeChecked();
  await saved;

  // AND "your profile was discovered" along with it — otherwise this journey no
  // longer checks what it claims.
  //
  // The reason lies one page further: the recruiter opens the scout below, the
  // person shows up in his hit list, and THAT is a notification of its own
  // (ADR-0033). It would come rightly — only every mail of this platform
  // carries the same sentence without saying what it is about (exactly what the
  // journey above checks). "No mail" could therefore not be told apart from
  // "the wrong mail", and from here on the journey would be a claim about
  // something else.
  const discovered = candidate.getByRole("switch", { name: /entdeckt wird/ });
  const alsoSaved = candidate.waitForResponse(
    (response) =>
      response.url().includes("/me/notification-preferences") &&
      response.request().method() === "PUT",
  );
  await discovered.click();
  await expect(discovered).not.toBeChecked();
  await alsoSaved;

  // Reload: saved is only what reached the server.
  await candidate.goto("/settings");
  await expect(
    candidate.getByRole("switch", { name: /Marktstatus sehen möchte/ })
  ).not.toBeChecked();
  await expect(
    candidate.getByRole("switch", { name: /entdeckt wird/ })
  ).not.toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  const since = Date.now();
  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  // The process runs anyway: the mail is a courtesy, not a condition.
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  const mail = await lastMailFor(candidateEmail, { after: since });
  expect(mail).toBeNull();

  await candidateContext.close();
  await recruiterContext.close();
});
