// Helpers for the E2E journeys: check reachability, create accounts, read mails.
//
// If the stack is not there, the tests skip themselves — the same pattern ADR-0011
// set for the Python integration tests. A red `make check` on a machine without
// Docker says nothing about the code; a green one that keeps quiet about a gap
// would be worse. Hence skip rather than pass.

import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

export const WEB_URL = process.env.E2E_WEB_URL ?? "http://localhost:5173";
export const IDENTITY_URL = process.env.E2E_IDENTITY_URL ?? "http://localhost:8001";
export const CONSENT_URL = process.env.E2E_CONSENT_URL ?? "http://localhost:8002";
export const PROFILE_URL = process.env.E2E_PROFILE_URL ?? "http://localhost:8003";
export const RESUME_URL = process.env.E2E_RESUME_URL ?? "http://localhost:8004";
export const PORTFOLIO_URL = process.env.E2E_PORTFOLIO_URL ?? "http://localhost:8005";
export const JOBS_URL = process.env.E2E_JOBS_URL ?? "http://localhost:8006";
export const APPLICATIONS_URL = process.env.E2E_APPLICATIONS_URL ?? "http://localhost:8007";
export const COMPANIES_URL = process.env.E2E_COMPANIES_URL ?? "http://localhost:8008";
export const TRANSFER_URL = process.env.E2E_TRANSFER_URL ?? "http://localhost:8009";
// 8011, not 8010: notification-service listens there. The mix-up stood here for
// months — the probe for "github-service" asked a different service with it,
// and a dead github-service looked healthy. Conversely, notification-service
// was never checked at all.
export const GITHUB_URL = process.env.E2E_GITHUB_URL ?? "http://localhost:8011";
export const ADVISOR_URL = process.env.E2E_ADVISOR_URL ?? "http://localhost:8013";
export const ASSESSMENT_URL = process.env.E2E_ASSESSMENT_URL ?? "http://localhost:8014";
export const NOTIFICATION_URL = process.env.E2E_NOTIFICATION_URL ?? "http://localhost:8010";

/** The gateway — the path the UI takes for its calls. */
export const GATEWAY_URL = process.env.E2E_GATEWAY_URL ?? "http://localhost:8090";
export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

const REQUIRED: ReadonlyArray<readonly [string, string, Record<string, string>?]> = [
  ["web", WEB_URL],
  ["identity-service", `${IDENTITY_URL}/health/live`],
  ["consent-service", `${CONSENT_URL}/health/live`],
  ["profile-service", `${PROFILE_URL}/health/live`],
  ["resume-service", `${RESUME_URL}/health/live`],
  ["portfolio-service", `${PORTFOLIO_URL}/health/live`],
  ["jobs-service", `${JOBS_URL}/health/live`],
  ["applications-service", `${APPLICATIONS_URL}/health/live`],
  ["companies-service", `${COMPANIES_URL}/health/live`],
  ["transfer-service", `${TRANSFER_URL}/health/live`],
  ["github-service", `${GITHUB_URL}/health/live`],
  ["notification-service", `${NOTIFICATION_URL}/health/live`],
  ["advisor-service", `${ADVISOR_URL}/health/live`],
  ["assessment-service", `${ASSESSMENT_URL}/health/live`],
  ["gateway", `${GATEWAY_URL}/health/live`],
  ["mailpit", `${MAILPIT_URL}/api/v1/messages?limit=1`],
];

// Only the positive finding is remembered — see `missingService`.
let stackIsUp = false;

// Patient, and that is the point. Before, there was ONE attempt with 2 seconds
// here — and because the result is cached below, a single hiccup switched off
// the ENTIRE rest of the run: 12 of 18 files skipped themselves although the
// stack was running (the same files passed one by one). The Vite dev server
// compiles on first access and easily needs more than two seconds under load.
//
// A service that answers after three seconds IS RUNNING. The question here is
// "is the stack up?", not "is it fast?" — and the wrong answer is expensive: it
// produces a report about a run that never happened.
const PROBE_TIMEOUT_MS = 10_000;
const PROBE_ATTEMPTS = 3;

// How long to wait for a mail.
//
// Since ADR-0025 a notification NO LONGER goes out synchronously in the request
// but via the outbox: write the row → dispatcher (5 s tick) → HTTP to
// identity-service → SMTP. The promise has since been "within the tick", not
// "immediately" — and 20 seconds were too tight for that. Under load exactly
// that happened: the run took 48.6 minutes instead of 4.6, and the journey fell
// over although the mail merely came later.
//
// The journeys check "the mail arrives", not "the mail arrives within 20
// seconds". A time frame that is too tight turns that into a statement about
// the machine's speed.
const MAIL_TIMEOUT_MS = 60_000;

/**
 * Time limit for ONE request to Mailpit — not for the wait as a whole.
 *
 * `fetch` has none of its own, and in a loop that checks a deadline that makes
 * the deadline ineffective: if a single request hangs, `Date.now()` is never
 * reached again. Measured from that: a journey that ran **15 minutes** on a
 * 150 s budget and in the end reported only "Test timeout" — without a place,
 * because no Playwright command was open and the report therefore had nothing
 * to show. The actual check (`expect(mail).toBeNull()`) never got its turn, so
 * the journey neither proved nor disproved what it is meant to.
 *
 * Five seconds are plenty: Mailpit answers in 2 ms, measured. The number is a
 * limit against hanging, not an expectation of speed.
 */
const MAILPIT_REQUEST_TIMEOUT_MS = 5_000;

/**
 * One request to Mailpit. `null` means "this one request did not answer" — the
 * calling loop tries again until ITS deadline runs out. A failure here must not
 * knock anything over: it is a statement about an HTTP call, not about the mail.
 */
async function mailpit<T>(path: string): Promise<T | null> {
  try {
    const res = await fetch(`${MAILPIT_URL}${path}`, {
      signal: AbortSignal.timeout(MAILPIT_REQUEST_TIMEOUT_MS),
    });
    if (!res.ok) return null;
    return (await res.json()) as T;
  } catch {
    return null;
  }
}

async function reachable(url: string, headers?: Record<string, string>): Promise<boolean> {
  for (let attempt = 1; attempt <= PROBE_ATTEMPTS; attempt += 1) {
    try {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
      const res = await fetch(url, { signal: controller.signal, headers });
      clearTimeout(timer);
      if (res.ok) return true;
    } catch {
      // Next attempt — an aborted call is no proof.
    }
    if (attempt < PROBE_ATTEMPTS) {
      await new Promise((resolve) => setTimeout(resolve, 500 * attempt));
    }
  }
  return false;
}

/** `null` when everything runs — otherwise the reason for skipping.
 *
 * Only a **positive** result is cached. A negative one is the expensive answer:
 * it skips a file, and across the run possibly all of them. If the stack is
 * really missing, the repetition costs one attempt per file against the first
 * unreachable service — affordable. If it merely hung once, the remaining files
 * run.
 */
export async function missingService(): Promise<string | null> {
  if (stackIsUp) return null;
  for (const [name, url, headers] of REQUIRED) {
    if (!(await reachable(url, headers))) {
      return name;
    }
  }
  stackIsUp = true;
  return null;
}

/** Call once per file; skips it when the stack is missing. */
export function skipWithoutStack(): void {
  test.beforeAll(async () => {
    const missing = await missingService();
    test.skip(
      missing !== null,
      `${missing} is not reachable — start the stack with "docker compose up"`
    );
  });
}

function nonce(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/** Unique per run: the stack is not reset between runs. */
export function uniqueEmail(domain: string): string {
  return `e2e-${nonce()}@${domain}`;
}

/**
 * A domain that never existed in this stack before.
 *
 * Needed for everything that creates a company: the domain is claimed and taken
 * from then on (ADR-0019). With a fixed domain the test passes exactly once and
 * fails from the second run on over a conflict that has nothing to do with what
 * it checks.
 */
export function uniqueCompanyDomain(): string {
  return `employer-${nonce()}.example`;
}

interface MailpitMessage {
  ID: string;
  Subject: string;
  To: Array<{ Address: string }>;
}

/**
 * A token from the mail of this kind last delivered to `address`.
 *
 * The subject is part of the search, not only the address. Before, the newest
 * mail to the address was taken and some `token=` read out of it — since there
 * are two kinds of links (confirmation and invitation), that picked sometimes
 * the one and sometimes the other, depending on which mail had already arrived
 * when looking. The test then failed at a place that had nothing to do with the
 * cause.
 *
 * It waits instead of looking once: sending runs after the commit and therefore
 * after the HTTP answer the browser reacted to.
 */
async function tokenFromMail(
  address: string,
  subjectPart: string,
  linkPath: string
): Promise<string> {
  const pattern = new RegExp(`${linkPath}\\?token=([A-Za-z0-9_-]+)`);
  const deadline = Date.now() + MAIL_TIMEOUT_MS;
  while (Date.now() < deadline) {
    // Ask for the address SPECIFICALLY, do not page through the newest 50.
    //
    // Mailpit collects hundreds of messages over a whole run. Whoever looks only
    // at the newest 50 no longer finds a mail once fifty others arrived between
    // its sending and the look — and that happens in every journey that waits
    // for something slow. The test then reported "no mail" and so blamed the
    // product for a window that was too small.
    const list = await mailpit<{ messages?: MailpitMessage[] }>(
      `/api/v1/search?query=${encodeURIComponent(`to:${address}`)}&limit=50`
    );
    const hit = (list?.messages ?? []).find((message) =>
      message.Subject.includes(subjectPart)
    );
    if (hit !== undefined) {
      const body = await mailpit<{ Text?: string; HTML?: string }>(`/api/v1/message/${hit.ID}`);
      const token = pattern.exec(`${body?.Text ?? ""}${body?.HTML ?? ""}`)?.[1];
      if (token !== undefined) return token;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`No ${subjectPart} mail for ${address} found in Mailpit`);
}

export function verificationTokenFor(address: string): Promise<string> {
  return tokenFromMail(address, "bestätige deine E-Mail-Adresse", "/verify");
}

export function invitationTokenFor(address: string): Promise<string> {
  return tokenFromMail(address, "eingeladen", "/invitation");
}

interface MailpitDetail {
  Text?: string;
  HTML?: string;
  Subject?: string;
}


/**
 * The mail last delivered to `address` — without a filter on the subject.
 *
 * Unlike `tokenFromMail`: there a particular kind is searched for; here the
 * point is to check WHAT arrives at all. A filter would answer the question
 * before it is asked.
 */
export async function lastMailFor(
  address: string,
  { after = 0 }: { after?: number } = {}
): Promise<{ subject: string; text: string } | null> {
  const deadline = Date.now() + MAIL_TIMEOUT_MS;
  while (Date.now() < deadline) {
    // See `tokenFromMail`: ask for the address specifically, not the newest 50.
    // It weighs heavier here — the erasure cascade's final mail comes minutes
    // after the trigger, and in that time fifty others easily arrive. The test
    // then reported "the cascade never finished" although it had.
    const list = await mailpit<{ messages?: (MailpitMessage & { Created?: string })[] }>(
      `/api/v1/search?query=${encodeURIComponent(`to:${address}`)}&limit=50`
    );
    const hit = (list?.messages ?? []).find(
      (message) => new Date(message.Created ?? 0).getTime() >= after
    );
    if (hit !== undefined) {
      const body = await mailpit<MailpitDetail>(`/api/v1/message/${hit.ID}`);
      return { subject: hit.Subject, text: `${body?.Text ?? ""}${body?.HTML ?? ""}` };
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return null;
}


/**
 * The password of every test account. One for all: it checks nothing, it only
 * has to satisfy the rules.
 */
export const E2E_PASSWORD = "e2e-Password-with-Length-1!";

export async function registerAndConfirm(
  page: Page,
  email: string,
  displayName: string,
  companyName?: string,
  occupationalField?: string
): Promise<void> {
  await page.goto("/register");
  // Since E2.6 a company comes into being ONLY here: the choice is made at
  // registration, it is created at confirmation. There is no "create a company"
  // page any more, which these journeys used to go through.
  if (companyName !== undefined) {
    await page.getByRole("radio", { name: "Für ein Unternehmen" }).check();
    await page.getByLabel("Name des Unternehmens").fill(companyName);
  }
  // Wait first, then type: `fill()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the larger expect budget. Under load the journey
  // otherwise failed at the field instead of at what it checks — the same lesson
  // that already stands further down for clicking.
  await expect(page.getByLabel(/E-Mail/i)).toBeVisible();
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).first().fill(E2E_PASSWORD);
  // Required field. If it is missing, native form validation blocks submitting
  // silently — no POST, no message, and the test hangs on the mail that never
  // comes. That is exactly how the first journey failed on its first run.
  await page.getByLabel(/Anzeigename/i).fill(displayName);
  // Optional (ADR-0039) — hence only when a journey explicitly asks for it. The
  // preselection is "Keine Angabe" (not stated), and whoever leaves it gets the
  // neutral view.
  if (occupationalField !== undefined) {
    await page.getByRole("combobox", { name: "Berufsfeld" }).click();
    await page.getByRole("option", { name: occupationalField, exact: true }).click();
  }
  await page.getByRole("button", { name: /Registrieren/i }).click();
  // The answer is deliberately the same for known and unknown addresses — so
  // this does not wait for a success message but for the mail, which exists
  // only for a genuinely new account.
  const token = await verificationTokenFor(email);
  await page.goto(`/verify?token=${token}`);
  // EXACTLY the success heading, letter for letter.
  //
  // The page has three: "Wird bestätigt…" (loading), "E-Mail bestätigt" (done)
  // and "Bestätigung fehlgeschlagen" (failed). A soft /bestätigt/i ALSO matches
  // the first — so the helper was satisfied while the confirmation was still
  // running or just failing, and the test carried on. Signing in then answered
  // "email not confirmed", at a place that had nothing to do with the cause.
  //
  // The predecessor of this comment warned about exactly this fault in another
  // form (/anmelden/i matched the link in the header). The lesson is the same
  // and paid for twice here: a success message is checked letter for letter,
  // not with a substring.
  //
  // And as with signing in, it waits for BOTH outcomes. Waiting only for the
  // hoped-for one meant: 30 seconds, then "element(s) not found" — a message
  // that does not distinguish whether the confirmation failed or the page was
  // still loading. That is exactly how this step fell over last time.
  const confirmed = page.getByRole("heading", { name: "E-Mail bestätigt", exact: true });
  const failed = page.getByRole("heading", { name: /Bestätigung fehlgeschlagen/i });
  try {
    await expect(confirmed.or(failed).first()).toBeVisible();
  } catch {
    const seen = await page
      .locator("main")
      .first()
      .innerText()
      .catch(() => "(no page readable)");
    throw new Error(
      `Confirmation for ${email}: neither success nor failure appeared.\n` +
        `URL: ${page.url()}\nSeite:\n${seen.slice(0, 500)}`
    );
  }
  if (await failed.isVisible()) {
    const detail = await page
      .locator("main")
      .first()
      .innerText()
      .catch(() => "");
    throw new Error(`Confirmation for ${email} failed: ${detail.slice(0, 300)}`);
  }
  // An account can be confirmed AND the company still not come into being: if
  // the domain is already claimed, the account stays active and the page shows
  // a notice. Without this check the journey would carry on and fall over steps
  // later at a place that has nothing to do with the cause — the same fault the
  // long comment above the success heading describes.
  if (companyName !== undefined) {
    const created = page.getByText(`${companyName} ist angelegt`);
    const refused = page.getByRole("alert");
    await expect(created.or(refused).first()).toBeVisible();
    if (await refused.isVisible()) {
      throw new Error(
        `Company "${companyName}" for ${email} was NOT created: ` +
          (await refused.innerText().catch(() => "(no text)")).slice(0, 300)
      );
    }
  }
  await expect(confirmed).toBeVisible();
}

/**
 * Sign in — and on a failure, say what it was.
 *
 * Before, this helper waited bluntly for the link "Mein Profil" and after 30
 * seconds ran into a timeout that revealed nothing: not whether sign-in was
 * refused, not whether the page loaded at all. Exactly this failure occurred
 * several times in suite runs and was equally uninformative every time.
 *
 * Now it waits for BOTH outcomes — success or error message — and if neither
 * occurs, the exception says what stood on the page instead. That does not fix
 * the flaky candidate; it makes sure the next failure explains it.
 */
export async function login(page: Page, email: string): Promise<void> {
  await page.goto("/login");
  // Wait first, then type: `fill()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the larger expect budget. Under load the journey
  // otherwise failed at the field instead of at what it checks — the same lesson
  // that already stands further down for clicking.
  await expect(page.getByLabel(/E-Mail/i)).toBeVisible();
  await page.getByLabel(/E-Mail/i).fill(email);
  await page.getByLabel(/Passwort/i).fill(E2E_PASSWORD);
  // The SUBMIT button, not just any button with "Anmelden" in its name: the
  // header has one that opens a menu.
  await page.locator('button[type="submit"]').click();

  // The account menu's summary instead of "Mein Profil": the link now lives IN
  // the menu and is not visible while collapsed. The summary exists only when
  // signed in and is always visible — a better signal than an entry that lies
  // behind a disclosure.
  const signedIn = page.getByRole("button", { name: "Mein Konto" });
  const failure = page.getByRole("alert");
  try {
    await expect(signedIn.or(failure).first()).toBeVisible();
  } catch {
    const seen = await page
      .locator("main")
      .first()
      .innerText()
      .catch(() => "(no page readable)");
    throw new Error(
      `Sign-in as ${email}: neither account menu nor error message appeared.\n` +
        `URL: ${page.url()}\nSeite:\n${seen.slice(0, 500)}`
    );
  }
  if (await failure.isVisible()) {
    throw new Error(`Sign-in as ${email} refused: ${await failure.innerText()}`);
  }
  await expect(signedIn).toBeVisible();
}


/**
 * An entry in a select field — for MUI's own, not for a native one.
 *
 * <strong>`selectOption` does not work here.</strong> It sets the value of a
 * real `<select>`; MUI draws a `role="combobox"` instead and opens a
 * `role="listbox"`. The way there is therefore a person's: open it, click the
 * entry.
 *
 * The fields were native until 03.09.2026 — and that cost the look: macOS lays
 * its own window over the field and shifts it left so that the chosen entry
 * sits over the field. Different on every system, and nowhere like the rest of
 * the UI.
 */
export async function pickOption(
  page: Page,
  label: RegExp | string,
  option: string
): Promise<void> {
  await page.getByLabel(label).click();
  await page.getByRole("option", { name: option, exact: true }).click();
}

/**
 * Wait until the switch to a company really holds.
 *
 * The choice only sets it off: the server issues a new token, the cookie is
 * replaced, the session reloaded. Clicking on immediately wins the race about
 * every second time. The company menu appears only with an active tenant and is
 * therefore the honest signal.
 */
export async function switchToCompany(page: Page, companyName: string): Promise<void> {
  await page.goto("/");
  await pickOption(page, /Handeln als/i, companyName);
  await expect(page.getByRole("button", { name: "Unternehmen" })).toBeVisible();
}
