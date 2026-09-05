// Die Benachrichtigung durch den echten Stack — und was sie NICHT sagt.
//
// Der Integrationstest belegt den Inhalt am Versandpfad im Prozess. Hier läuft
// er über SMTP nach Mailpit, wie in Produktion: identity-service schreibt,
// transfer-service hat ausgelöst, und dazwischen liegt echtes HTTP mit dem
// gemeinsamen Geheimnis. Ein fehlendes `WORKER_NOTIFY_SECRET` in einem der
// beiden Container wäre auf keiner anderen Ebene sichtbar.

import { expect, test } from "@playwright/test";

import {
  lastMailFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  waehleImFeld,
} from "./stack";

skipWithoutStack();

test("eine Anfrage erreicht die Person per Mail — und die Mail verrät nicht, worum es geht", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Melder ${Date.now()}`;
  const headline = `E2E Melde-Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // Ab hier zählt nur, was nach diesem Zeitpunkt zugestellt wird — die
  // Bestätigungsmail von vorhin ginge sonst als Treffer durch.
  const since = Date.now();

  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  const mail = await lastMailFor(candidateEmail, { after: since });
  expect(mail).not.toBeNull();

  const text = `${mail?.subject ?? ""}\n${mail?.text ?? ""}`
    .toLowerCase()
    // Der Markenname raus: „WorkerTransfer" enthält „transfer".
    .replace(/workertransfer/g, "");
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
  // Aber ein Weg zurück steht drin — sonst wäre die Mail wirklich nutzlos.
  expect(mail?.text ?? "").toContain("http");

  await candidateContext.close();
  await recruiterContext.close();
});

test("wer die Art abbestellt, bekommt dazu keine Mail mehr", async ({ browser }) => {
  // Dreifaches Budget, und zwar wegen der FORM dieser Reise, nicht wegen der
  // Maschine: sie ist die einzige, die ein volles Mail-Fenster (60 s) absichtlich
  // mit Warten auf nichts verbringt — eine Abwesenheit lässt sich nur durch
  // Verstreichen beweisen. Dazu kommen zwei Registrierungen mit Mail-Umlauf,
  // eine Unternehmensanlage und ein Wechsel. 150 s reichen dafür nicht, und das
  // Fenster zu verkürzen wäre keine Reparatur, sondern eine schwächere Aussage.
  test.slow();
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Stumm ${Date.now()}`;
  const headline = `E2E Stumm-Kandidat ${Date.now()}`;

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: /Speichern/i }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // Abbestellen — der Schalter wirkt sofort, es gibt keinen Speichern-Knopf.
  await candidate.goto("/settings");
  const marketSwitch = candidate.getByRole("switch", { name: /Marktstatus sehen möchte/ });
  await expect(marketSwitch).toBeChecked();
  await marketSwitch.click();
  await expect(marketSwitch).not.toBeChecked();
  // Neu laden: gespeichert ist nur, was den Server erreicht hat.
  await candidate.goto("/settings");
  await expect(
    candidate.getByRole("switch", { name: /Marktstatus sehen möchte/ })
  ).not.toBeChecked();

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  const since = Date.now();
  await recruiter.goto("/candidates");
  const card = recruiter.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  // Der Vorgang läuft trotzdem: die Mail ist Höflichkeit, nicht Bedingung.
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  const mail = await lastMailFor(candidateEmail, { after: since });
  expect(mail).toBeNull();

  await candidateContext.close();
  await recruiterContext.close();
});
