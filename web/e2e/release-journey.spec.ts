// The way to the current employer (ADR-0050): the employer allows inquiries,
// the person opens the route on its careers page, the hiring company asks for
// an earlier release with a fee, the employer accepts — and she agrees last.

import { expect, test, type Page } from "@playwright/test";

import {
  login,
  pickOption,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

async function actAs(page: Page, email: string, name: string): Promise<void> {
  await login(page, email);
  await page.goto("/");
  await pickOption(page, /Handeln als/i, name);
  await expect(page.getByRole("button", { name: "Unternehmen" })).toBeVisible();
}

test("an earlier release is negotiated between two companies, and the person decides last", async ({ browser }) => {
  const stamp = Date.now();
  const employerName = `E2E Employer ${stamp}`;
  const hiringName = `E2E Hiring ${stamp}`;
  const headline = `E2E Release candidate ${stamp}`;

  // The current employer: a careers page, and the switch on.
  const employerContext = await browser.newContext();
  const employer = await employerContext.newPage();
  const employerEmail = uniqueEmail(uniqueCompanyDomain());
  await registerAndConfirm(employer, employerEmail, "E2E Employer admin", employerName);
  await actAs(employer, employerEmail, employerName);
  await employer.goto("/company/profile");
  await employer.getByLabel(/Anzeigename/i).fill(employerName);
  await employer.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(employer.getByText(/Profil gespeichert/i)).toBeVisible();
  const slug = await employer.evaluate(async () => {
    const res = await fetch(`${window.location.origin.replace(":5173", ":8008")}/companies/me/profile`, {
      credentials: "include",
    });
    return ((await res.json()) as { slug: string }).slug;
  });
  await employer.goto("/company/transfers");
  const allow = employer.getByRole("switch", { name: /Anfragen anderer Firmen zu unserem Personal zulassen/ });
  await expect(allow).not.toBeChecked();
  await allow.click();
  await expect(allow).toBeChecked();

  // The person: profile released, listening, employed — and the route opened
  // on her employer's careers page, where she sees which company she names.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  const candidateEmail = uniqueEmail("candidate.example");
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();
  await candidate.goto("/market");
  await candidate.getByRole("radio", { name: /Ich höre zu/ }).check();
  await candidate.getByRole("checkbox", { name: /Ich arbeite gerade irgendwo/ }).check();
  await candidate.getByRole("button", { name: /^Speichern$/ }).click();
  await expect(candidate.getByText(/Marktstatus gespeichert/i)).toBeVisible();

  await candidate.goto(`/careers/${slug}`);
  await candidate.getByRole("button", { name: /Weg über deinen Arbeitgeber/ }).click();
  // What follows stands before the button.
  await expect(candidate.getByText(/Dein Arbeitgeber erfährt nichts, solange niemand fragt/)).toBeVisible();
  await candidate.getByRole("button", { name: "Als meinen Arbeitgeber nennen und Weg öffnen" }).click();
  await expect(candidate.getByText(new RegExp(`Der Weg zu ${employerName} ist offen`))).toBeVisible();

  // Opening the route tells the employer nothing.
  await employer.goto("/company/transfers");
  await expect(employer.getByText("Keine Anfrage.")).toBeVisible();

  // The hiring company finds her, she releases her market status, it shows
  // interest, she accepts the conversation.
  const hiringContext = await browser.newContext();
  const hiring = await hiringContext.newPage();
  const hiringEmail = uniqueEmail(uniqueCompanyDomain());
  await registerAndConfirm(hiring, hiringEmail, "E2E Hiring admin", hiringName);
  await actAs(hiring, hiringEmail, hiringName);
  await hiring.goto("/scout");
  const card = hiring.locator("li").filter({ hasText: headline });
  await expect(card).toBeVisible();
  await card.getByRole("button", { name: /Marktstatus anfragen/i }).click();
  await expect(card.getByText(/Marktstatus angefragt/i)).toBeVisible();

  await candidate.goto("/market");
  const row = candidate.locator("li").filter({ hasText: /ob du ansprechbar bist/i });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Freigeben/i }).click();
  await expect(row.getByText(/Freigegeben/i)).toBeVisible();

  await hiring.goto("/scout");
  const openCard = hiring.locator("li").filter({ hasText: headline });
  await expect(openCard).toBeVisible();
  await openCard.getByRole("button", { name: /Interesse zeigen/i }).click();
  await expect(openCard.getByText(/Interesse hinterlegt/i)).toBeVisible();

  await candidate.goto("/transfers");
  await candidate.getByRole("button", { name: /Gespräch annehmen/i }).click();
  await expect(candidate.getByRole("heading", { name: /Ihr seid im Gespräch/ })).toBeVisible();

  // Both consents stand: the hiring company sees the route — and asks.
  await hiring.goto("/company/transfers");
  const negotiation = hiring.getByRole("region", { name: "Frühere Freigabe beim Arbeitgeber" });
  await expect(negotiation.getByText(new RegExp(`${employerName} lässt Anfragen zu`))).toBeVisible();
  await negotiation.getByLabel(/Freigabe zum/).fill("2026-12");
  await negotiation.getByLabel("Ablöse in Euro").fill("5000");
  await negotiation.getByRole("button", { name: "Beim Arbeitgeber anfragen" }).click();
  await expect(negotiation.getByText(/Angefragt/)).toBeVisible();

  // The employer sees her name only now, and accepts the terms.
  await employer.goto("/company/transfers");
  const inquiry = employer.locator("li").filter({ hasText: "Zu E2E Candidate" });
  await expect(inquiry).toBeVisible();
  await expect(inquiry.getByText("5.000,00 €")).toBeVisible();
  await inquiry.getByRole("button", { name: "Bedingungen annehmen" }).click();
  await expect(inquiry.getByText(/es fehlt die Zustimmung der Person/)).toBeVisible();

  // She decides last.
  await candidate.goto("/transfers");
  const hers = candidate.getByRole("region", { name: "Frühere Freigabe beim Arbeitgeber" });
  await expect(hers.getByText(/Erst mit deiner Zustimmung gilt es/)).toBeVisible();
  await hers.getByRole("button", { name: "Zustimmen" }).click();
  await expect(hers.getByText(/Vereinbart\. Den Aufhebungsvertrag/)).toBeVisible();

  // And she can close the route on her market page.
  await candidate.goto("/market");
  await expect(candidate.getByText(new RegExp(`Offen zu ${employerName}`))).toBeVisible();
  await candidate.getByRole("button", { name: "Weg schließen" }).click();
  await expect(candidate.getByText(/Kein Weg offen/)).toBeVisible();

  await candidateContext.close();
  await employerContext.close();
  await hiringContext.close();
});
