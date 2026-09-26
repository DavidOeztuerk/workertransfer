// The whole path of an erasure: request it → the cascade runs → the data is
// gone → the proof in the ledger still stands (ADR-0027).
//
// This journey checks something none of the others can: that a promise the UI
// makes is in the end also kept in the DATABASES — across seven service
// boundaries and without anybody helping along. Exactly this connection was
// missing while ROADMAP 10.5 still stood at ⛔: the endpoint said "accepted",
// and nobody did anything.
//
// WHY THE TOKEN STILL CARRIES AFTER THE ERASURE — and why that is not a loophole
// but the only way to look afterwards at all: profile- and consent-service
// check the SIGNATURE of an access token, they do not ask identity-service
// about the session (ADR-0015). The token therefore survives the erasure until
// it expires. For the person that changes nothing — their browser gets an empty
// cookie on erasure and can never sign in again. For this test it is the probe
// with which it can be proved that the rows really fall: otherwise it would
// have to know the erasure secret, and a test suite holding the erasure secret
// would be a worse trade.

import { expect, test } from "@playwright/test";
import type { APIRequestContext, Page } from "@playwright/test";

import {
  CONSENT_URL,
  E2E_PASSWORD,
  IDENTITY_URL,
  MAILPIT_URL,
  PROFILE_URL,
  lastMailFor,
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueEmail,
} from "./stack";

skipWithoutStack();

// The cascade runs over three dispatcher ticks (default 5 s): seven recipients
// → final message → account. Generously sized, because the question is "does it
// arrive?" and not "is the machine fast?" — a frame that is too tight turns it
// into a statement about the machine's load.
const CASCADE_TIMEOUT_MS = 120_000;

async function accessTokenOf(page: Page): Promise<string> {
  const cookies = await page.context().cookies();
  const access = cookies.find((cookie) => cookie.name === "access");
  expect(access, "no access cookie — the sign-in did not hold").toBeTruthy();
  return access!.value;
}

async function profileOf(
  api: APIRequestContext,
  token: string
): Promise<Record<string, unknown> | null> {
  const res = await api.get(`${PROFILE_URL}/profiles/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok()) return null;
  return (await res.json()) as Record<string, unknown> | null;
}

async function ledgerOf(api: APIRequestContext, token: string): Promise<string[]> {
  const res = await api.get(`${CONSENT_URL}/consent/me/history`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok()) return [];
  const events = (await res.json()) as { action: string }[];
  return events.map((event) => event.action);
}

/**
 * A confirmation token from a mail delivered AFTER `mark`.
 *
 * The timestamp is the whole point: after an erasure the old confirmation mail
 * to the same address still lies in Mailpit, and its token is long used up.
 * Without the barrier the test picks it — and fails on a "Bestätigung
 * fehlgeschlagen" (confirmation failed) that has nothing to do with the matter.
 */
async function verificationTokenAfter(
  email: string,
  mark: number,
  budgetMs: number
): Promise<string | null> {
  const deadline = Date.now() + budgetMs;
  while (Date.now() < deadline) {
    const list = (await (await fetch(`${MAILPIT_URL}/api/v1/messages?limit=50`)).json()) as {
      messages?: { ID: string; Subject: string; Created: string; To: { Address: string }[] }[];
    };
    const hit = (list.messages ?? []).find(
      (message) =>
        message.Subject.includes("bestätige deine E-Mail-Adresse") &&
        message.To.some((to) => to.Address.toLowerCase() === email.toLowerCase()) &&
        new Date(message.Created).getTime() >= mark
    );
    if (hit !== undefined) {
      const body = (await (await fetch(`${MAILPIT_URL}/api/v1/message/${hit.ID}`)).json()) as {
        Text?: string;
        HTML?: string;
      };
      const token = /\/verify\?token=([A-Za-z0-9_-]+)/.exec(
        `${body.Text ?? ""}${body.HTML ?? ""}`
      )?.[1];
      if (token !== undefined) return token;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return null;
}

test("an erasure is requested, runs through — and the proof remains", async ({
  browser,
  request,
}) => {
  test.setTimeout(CASCADE_TIMEOUT_MS + 120_000);

  const email = uniqueEmail("example.com");
  const headline = `E2E Erasure candidate ${Date.now()}`;

  const context = await browser.newContext();
  const page = await context.newPage();
  await registerAndConfirm(page, email, "E2E Erasure candidate");
  await login(page, email);

  // Create something that can disappear — otherwise the journey would in the
  // end check that nothing is gone where nothing ever was.
  await page.goto("/profile");
  await page.getByLabel(/Überschrift/i).fill(headline);
  await page.getByLabel("Standort", { exact: true }).fill("Bremen");
  await page.getByLabel(/Fähigkeiten/i).fill("Python, Go");
  await page.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(page.getByText(/Profil gespeichert/i)).toBeVisible();

  // Grant a release: later it is the EVIDENCE that the erasure happened.
  const release = page.getByRole("switch");
  await expect(release).toBeEnabled();
  await release.click();
  await expect(release).toBeChecked();

  const token = await accessTokenOf(page);

  // The state BEFORE the erasure — otherwise "is gone" says nothing at the end.
  expect(await profileOf(request, token)).not.toBeNull();
  expect(await ledgerOf(request, token)).toContain("GRANT");

  const startedAt = Date.now();

  await page.goto("/delete-account");

  // Wait for the BUTTON, not the heading: "Konto löschen" also stands above the
  // sign-in prompt this page shows while the session is still loading. Waiting
  // for it would mean reading the text before it exists — exactly the substring
  // fault `stack.ts` has already paid for dearly twice.
  await expect(page.getByRole("button", { name: /^Konto löschen$/ })).toBeVisible();

  // The page says BEFORE the click what happens — not in a privacy policy
  // (ADR-0024, the same rule).
  const pageText = await page.locator("main").first().innerText();
  expect(pageText).toMatch(/unwiderruflich/i);
  expect(pageText).toMatch(/nicht sofort/i);
  expect(pageText, "the uncomfortable consequence belongs visibly on this page").toMatch(/eingestellt/i);

  // Two deliberate steps, no obstacle course: the first click deletes nothing.
  await page.getByRole("button", { name: /^Konto löschen$/ }).click();
  await expect(page.getByRole("button", { name: /Ja, endgültig löschen/i })).toBeVisible();
  expect(await profileOf(request, token), "the first click must delete nothing").not.toBeNull();

  await page.getByRole("button", { name: /Ja, endgültig löschen/i }).click();

  // "Accepted and running" — not "done".
  const done = page.getByRole("status");
  await expect(done).toBeVisible();
  await expect(done).toContainText(/läuft/i);

  // Signed out at once: from here on nothing happens under this name any more.
  await expect(page.getByRole("button", { name: /Einstellungen und Anmeldung/i })).toBeVisible();

  // ---- The cascade runs ----------------------------------------------------

  await expect
    .poll(async () => await profileOf(request, token), {
      timeout: CASCADE_TIMEOUT_MS,
      message: "the profile is still there after the erasure",
    })
    .toBeNull();

  // ---- And now the proof ---------------------------------------------------

  // The data is gone.
  expect(await profileOf(request, token)).toBeNull();

  // The evidence still stands: the chain of grant AND deletion.
  await expect
    .poll(async () => await ledgerOf(request, token), {
      timeout: CASCADE_TIMEOUT_MS,
      message: "the DELETE row is missing from the ledger",
    })
    .toContain("DELETE");
  const actions = await ledgerOf(request, token);
  expect(actions, "the grant must remain — otherwise nothing is provable").toContain(
    "GRANT"
  );

  // Exactly one final message, and it says only THAT it is done.
  const mail = await lastMailFor(email, { after: startedAt });
  expect(mail, "no final message").not.toBeNull();
  expect(mail!.subject).toMatch(/gelöscht/i);
  // No list of what the person had: that would be a copy of the data in a
  // mailbox that may not be hers alone.
  expect(mail!.text).not.toContain(headline);

  await context.close();
});

test("after the erasure sign-in is closed — and the address is free again", async ({
  browser,
  request,
}) => {
  test.setTimeout(CASCADE_TIMEOUT_MS + 120_000);

  const email = uniqueEmail("example.com");
  const context = await browser.newContext();
  const page = await context.newPage();
  await registerAndConfirm(page, email, "E2E Second account");
  await login(page, email);

  const startedAt = Date.now();

  await page.goto("/delete-account");
  await expect(page.getByRole("button", { name: /^Konto löschen$/ })).toBeVisible();
  await page.getByRole("button", { name: /^Konto löschen$/ }).click();
  await page.getByRole("button", { name: /Ja, endgültig löschen/i }).click();
  await expect(page.getByRole("status")).toBeVisible();

  // The done signal is the final message — not a `DELETE` row in the ledger.
  //
  // This account NEVER granted a release, and so no `DELETE` row comes into
  // being there either: there is one per capability the person once held
  // (ADR-0027 §5). About somebody who allowed nothing, nothing at all stands in
  // the ledger afterwards — and that is right, not a missing case.
  const done = await lastMailFor(email, { after: startedAt });
  expect(done, "no final message — the cascade never finished").not.toBeNull();
  expect(done!.subject).toMatch(/gelöscht/i);

  // What is actually checked: the address is free again.
  //
  // The alternative would be a permanent remnant of precisely the item that was
  // to be deleted — an email hash that lives on to recognise new registrations
  // ("no tombstone", ADR-0027).
  //
  // TRIED REPEATEDLY, and that is not a blemish but the design: between "done"
  // being said and the account row falling lies one dispatcher tick (§6) — the
  // message needs the address that is about to be deleted, so it necessarily
  // goes out before. In exactly this window the address is still taken.
  // Measured: the account falls about five seconds after the message.
  //
  // It is checked on the strongest sign available: a NEW confirmation mail
  // arrives again. A refused sign-in would be NO proof — that is refused already
  // while the account is merely disabled.
  let token: string | null = null;
  const deadline = Date.now() + CASCADE_TIMEOUT_MS;
  while (token === null && Date.now() < deadline) {
    const mark = Date.now();
    await request.post(`${IDENTITY_URL}/auth/register`, {
      data: { email, password: E2E_PASSWORD, display_name: "E2E Second account, second attempt" },
    });
    token = await verificationTokenAfter(email, mark, 6_000);
  }
  expect(token, "the address never became free again after the erasure").not.toBeNull();

  const fresh = await context.newPage();
  await fresh.goto(`/verify?token=${token}`);
  await expect(fresh.getByRole("heading", { name: "E-Mail bestätigt", exact: true })).toBeVisible();

  await context.close();
});
