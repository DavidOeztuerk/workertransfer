// "Meine Freigaben" (my releases) through the real stack.
//
// The point of the page is that it shows EVERYTHING — including what appears on
// no domain page. That is why releases come into being here in three different
// ways (profile switch, market status request, portfolio switch), and the page
// has to know all three.

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

test("one page shows every release — including those that appear nowhere else", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Releases ${Date.now()}`;
  const headline = `E2E Releases candidate ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);

  // Before, nothing stands there — and the page says so.
  await candidate.goto("/consents");
  await expect(candidate.getByText(/Niemand sieht etwas von dir/)).toBeVisible();

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  /*
    The profile release now stands on this page as a SWITCH, not as a row with
    "Zurückziehen" (withdraw): it can be GRANTED here too, and showing the same
    release twice — once as a switch, once as a row — would leave nobody knowing
    which of the two holds.

    What is checked stays the same: what holds stands here. Only as the state of
    a switch instead of an entry in a list.
  */
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();

  // A company obtains a recipient-bound release.
  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // A company profile, so that the page shows a name instead of a UUID.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: "Speichern", exact: true }).click();
  // Wait for the CONFIRMATION, not for `/gespeichert/i`: while saving, the
  // button reads "Wird gespeichert…" (saving…), and the pattern matched it. The
  // journey then moved on while the request was still running — `goto()`
  // aborts it, and the profile was never written. Measured: the page later
  // showed "Marktstatus · Ein Unternehmen" instead of the company name, and
  // `company_profiles` held no row.
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  await recruiter.goto("/scout");
  const card = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(card).toBeVisible();
  // Wait first, then click. `click()` has only the actionTimeout (15 s); under
  // load — eleven containers, Vite and Chromium on one machine — the candidate
  // list takes longer, and the test failed at the click instead of at what it
  // checks. That is exactly how it fell over twice.
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  await candidate.goto("/market");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  // Both releases stand on ONE page — the global one as a switch, the
  // recipient-bound one as a row with the company name instead of a UUID.
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toBeVisible();

  // Withdrawing takes effect immediately — the company's next access is empty.
  const marketEntry = candidate.locator("li").filter({ hasText: "Marktstatus ·" });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(marketEntry).toBeVisible();
  await marketEntry.getByRole("button", { name: /Zurückziehen/i }).click();
  await expect(candidate.getByText(new RegExp(`Marktstatus · ${companyName}`))).toHaveCount(0);

  await recruiter.goto("/scout");
  const afterCard = recruiter.locator("li").filter({ hasText: headline });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(afterCard).toBeVisible();
  await expect(afterCard.getByText(/Marktstatus gerade nicht einsehbar/)).toBeVisible();

  // The profile still stands: exactly one release was withdrawn.
  await candidate.goto("/consents");
  await expect(
    candidate.getByRole("switch", { name: /Profil freigeben/ })
  ).toBeChecked();

  await candidateContext.close();
  await recruiterContext.close();
});


test("the search finds only what is released", async ({ browser }) => {
  const visibleEmail = uniqueEmail("candidate.example");
  const hiddenEmail = uniqueEmail("candidate.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Search ${Date.now()}`;
  const stamp = Date.now();
  const skill = `Searchskill${stamp}`;
  const visibleHeadline = `E2E Visible ${stamp}`;
  const hiddenHeadline = `E2E Hidden ${stamp}`;

  // Two people with THE SAME skill — one releases, one does not.
  const visibleContext = await browser.newContext();
  const visible = await visibleContext.newPage();
  await registerAndConfirm(visible, visibleEmail, "E2E Visible");
  await login(visible, visibleEmail);
  await visible.goto("/profile");
  await visible.getByLabel(/Überschrift/i).fill(visibleHeadline);
  await visible.getByLabel(/Fähigkeiten/i).fill(skill);
  await visible.getByLabel("Standort", { exact: true }).fill("Berlin");
  await visible.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(visible.getByText(/Profil gespeichert/i)).toBeVisible();
  await visible.getByRole("switch").click();
  await expect(visible.getByRole("switch")).toBeChecked();

  const hiddenContext = await browser.newContext();
  const hidden = await hiddenContext.newPage();
  await registerAndConfirm(hidden, hiddenEmail, "E2E Hidden");
  await login(hidden, hiddenEmail);
  await hidden.goto("/profile");
  await hidden.getByLabel(/Überschrift/i).fill(hiddenHeadline);
  await hidden.getByLabel(/Fähigkeiten/i).fill(skill);
  await hidden.getByLabel("Standort", { exact: true }).fill("Berlin");
  await hidden.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(hidden.getByText(/Profil gespeichert/i)).toBeVisible();
  // No click on the switch: this profile stays hidden.

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(skill);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  // The point: the same filter, two matching profiles, one hit.
  await expect(recruiter.getByText(visibleHeadline)).toBeVisible();
  await expect(recruiter.getByText(hiddenHeadline)).toHaveCount(0);

  // And a search without hits talks about the search, not about the platform.
  await recruiter.getByLabel("Fähigkeiten").fill(`Nothing${stamp}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();
  await expect(recruiter.getByText(/Auf diese Suche passt gerade niemand/)).toBeVisible();

  await visibleContext.close();
  await hiddenContext.close();
  await recruiterContext.close();
});


test("the data disclosure names every section — the empty ones too", async ({ browser }) => {
  const email = uniqueEmail("candidate.example");
  const stamp = Date.now();
  const headline = `E2E Disclosure ${stamp}`;

  const context = await browser.newContext();
  const person = await context.newPage();
  await registerAndConfirm(person, email, "E2E Disclosure");
  await login(person, email);

  await person.goto("/profile");
  await person.getByLabel(/Überschrift/i).fill(headline);
  await person.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(person.getByText(/Profil gespeichert/i)).toBeVisible();
  await person.getByRole("switch").click();
  await expect(person.getByRole("switch")).toBeChecked();

  await person.goto("/my-data");

  // Every section stands there — including those for which there is nothing.
  // "No resume" is a piece of information and would otherwise be missing.
  //
  // The list is a `<dl>`: the name in the `<dt>`, the information in the `<dd>`
  // beside it. Before, both were one text node ("profil — enthalten"), and a
  // check on it could not fail when the PAIRING slipped. This helper moves along
  // the same relation a screen reader uses.
  //
  // The section names ("profil", "lebenslauf", "freigaben verlauf") are keys of
  // the export file format and stay German; "enthalten" is the catalogue text.
  const entry = (section: string) =>
    person.getByText(section, { exact: true }).locator("xpath=following-sibling::dd[1]");
  await expect(entry("profil")).toHaveText("enthalten");
  await expect(entry("lebenslauf")).toHaveText("enthalten");
  await expect(entry("portfolio")).toHaveText("enthalten");
  await expect(entry("freigaben verlauf")).toHaveText("enthalten");
  // Nothing is missing: with the stack running there is no warning.
  await expect(person.getByRole("alert")).toHaveCount(0);

  // The file comes into being in the browser — the download proves it does.
  const download = person.waitForEvent("download");
  await person.getByRole("button", { name: /Als JSON herunterladen/i }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/^workertransfer-meine-daten-\d{4}-\d{2}-\d{2}\.json$/);

  await context.close();
});


// The writing aid (ADR-0024). What can be checked here is NOT the provider —
// that would need a secret and cost money. What can be checked is the state "not
// set up", and in every environment without a key that is the real one: no
// crash, no empty text, but a page that says so, and a profile text that stands
// unchanged.
test("without a configured provider one's own text stays untouched", async ({ page }) => {
  const email = uniqueEmail("aid.example");
  await registerAndConfirm(page, email, "E2E Writing Aid");
  await login(page, email);

  await page.goto("/profile");
  await page.getByLabel(/Überschrift/i).fill("Registered nurse");
  await page.getByLabel(/Über mich/i).fill("My own text.");
  await page.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(page.getByText(/Profil gespeichert/i)).toBeVisible();

  await page.getByRole("button", { name: /Vorschlag holen/i }).click();

  // The compose stack passes ANTHROPIC_API_KEY through as `Draft__ApiKey`,
  // empty by default, and CI has no key — so the state checked here is always
  // "not set up". Whoever starts locally with ANTHROPIC_API_KEY checks something
  // else with this test; that is the price of the suite needing no secret.
  //
  // An `.or()` condition stood here first and was worthless: the text field is
  // always visible, so it always matched.
  await expect(page.getByRole("alert")).toHaveText(/nicht verfügbar/i);
  // And the person's text stands unchanged — no empty field, no half draft.
  await expect(page.getByLabel(/Über mich/i)).toHaveValue("My own text.");
});

test("connecting GitHub demands a proof — and computes no grade", async ({ browser }) => {
  const email = uniqueEmail("candidate.example");
  const stamp = Date.now();

  const context = await browser.newContext();
  const person = await context.newPage();
  await registerAndConfirm(person, email, "E2E GitHub");
  await login(person, email);

  await person.goto("/github");

  // The promise stands on the page where somebody connects their account — not
  // in an ADR they never read.
  await expect(person.getByText(/Belege, keine Noten/)).toBeVisible();
  await expect(person.getByText(/Geholt wird nur, wenn du es auslöst/)).toBeVisible();

  // THE QUICK WAY COMES FIRST and asks for no name: GitHub reports the account
  // itself. Here it is only to be seen, not taken — the journey must not call
  // github.com.
  await expect(
    person.getByRole("button", { name: "Mit GitHub anmelden" })
  ).toBeVisible();

  // The manual way lies below and is expanded.
  await person.getByRole("button", { name: /Oder von Hand/ }).click();
  await person.getByLabel(/GitHub-Benutzername/).fill(`e2e-user-${stamp}`);
  await person.getByRole("button", { name: "Weiter" }).click();

  // No gist, no connection: a field "my GitHub name" without proof would be an
  // invitation to adorn oneself with somebody else's work.
  const challenge = person.locator(".github__challenge");
  await expect(challenge).toBeVisible();
  await expect(challenge).toHaveText(/^workertransfer-verify-/);

  await person.getByRole("button", { name: "Nachweis prüfen" }).click();
  // GitHub does not know this user — so it stays "not proven", and nothing is
  // shown.
  await expect(person.getByRole("alert")).toBeVisible();
  // AFTER THE RELOAD the manual way is closed again — the gist imposes itself on
  // nobody. What stands open is the named account together with a way out:
  // whoever mistyped would otherwise fail at sign-in (the name comparison
  // refuses) and would find the button for it behind a disclosure.
  await person.reload();
  await expect(person.getByText(/Genanntes Konto/)).toBeVisible();
  await expect(person.getByRole("button", { name: "Anderes Konto" })).toBeVisible();
  await expect(person.locator(".github__challenge")).toBeHidden();

  await person.getByRole("button", { name: /Oder von Hand/ }).click();
  await expect(person.locator(".github__challenge")).toBeVisible();

  await context.close();
});
