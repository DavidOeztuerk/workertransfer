// Jobs through the browser — with the one point that can only be checked here:
// a published advert is found by somebody WITHOUT an account too.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  switchToCompany,
  uniqueCompanyDomain,
  uniqueEmail,
  pickOption,
} from "./stack";

skipWithoutStack();

test("a published job is found even by somebody without an account", async ({ browser }) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Employer ${Date.now()}`;
  const title = `E2E Job ${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await pickOption(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // The company profile first: without it the job stays anonymous.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("What there is to do.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Hamburg");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  // Wait first, then click: `click()` has only the actionTimeout (15 s),
  // `expect(...).toBeVisible()` the more generous expect budget. Under load the
  // test otherwise failed at the click instead of at what it checks.
  await expect(row).toBeVisible();
  await expect(row.getByText(/Entwurf/)).toBeVisible();

  // An anonymous visitor: own context, no cookie, no sign-in.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  // For the public, a draft does not exist.
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  await anonymous.reload();
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(title)).toBeVisible();
  // And who is hiring stands beside it — for somebody without an account too.
  await expect(anonymous.getByText(companyName, { exact: true })).toBeVisible();

  // And the same job stands on the company's careers page — an address one can
  // pass on without the recipient needing an account. The recruiter fetches the
  // slug; he is signed in.
  const slug = await recruiter.evaluate(async () => {
    const res = await fetch(
      `${window.location.origin.replace(":5173", ":8008")}/companies/me/profile`,
      { credentials: "include" }
    );
    return ((await res.json()) as { slug: string }).slug;
  });

  await anonymous.goto(`/careers/${slug}`);
  await expect(anonymous.getByRole("heading", { name: companyName })).toBeVisible();
  await expect(anonymous.getByText(title)).toBeVisible();

  // "Bewerben" (apply) WITHOUT AN ACCOUNT — the path the page
  // `/jobs/{id}/apply` carried until PBI-6. It is gone (it was a switch with an
  // unreachable form beneath it), and its signed-out branch moved here: remember
  // the job first, then on to sign-in. It is checked here because the careers
  // page is the ONLY public place with this button, and the move would
  // otherwise leave it unchecked — exactly the kind of seam that tears silently.
  await anonymous.getByRole("button", { name: /Bewerben/i }).first().click();
  await expect(anonymous).toHaveURL(/\/login$/);
  await anonymous.goBack();

  // Closed means: gone again for the public.
  await row.getByRole("button", { name: /Schließen/i }).click();
  await expect(row.getByText("Geschlossen")).toBeVisible();

  // Explicitly back to the search: the anonymous browser is on the careers page
  // right now, and a reload() reloads that one. There is no search field there,
  // and the test waited for it until the time limit.
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await recruiterContext.close();
  await anonymousContext.close();
});

// The skill checklist. Checkable only here, because that is exactly the point:
// it comes into being in the browser from two answers of different services and
// stands in neither of them. No endpoint delivers it, so no endpoint test can
// show it.
test("the person sees the checklist — and nobody computes it on the server", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Checklist ${Date.now()}`;
  const title = `E2E Checklist job ${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await switchToCompany(recruiter, companyName);

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("What there is to do.");
  // "PostgreSQL" here, "postgres" in the profile in a moment: the vocabulary
  // (ADR-0023) must bring both to the same name, otherwise the comparison shows
  // the person a gap that does not exist.
  await recruiter.getByLabel(/Gesuchte Fähigkeiten/i).fill("Python, PostgreSQL, Go");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  // Without an account: the requirements stand there, but nobody is counted.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText("PostgreSQL")).toBeVisible();
  await expect(anonymous.getByText(/von 3 genannten/)).toHaveCount(0);

  // With a profile: the list is ticked off — and says what is missing.
  const candidateEmail = uniqueEmail("candidate.example");
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Candidate");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill("Backend developer");
  // Lower case, with spaces — and "postgres" instead of "PostgreSQL". Neither
  // may cost the comparison anything.
  await candidate.getByLabel(/Fähigkeiten/i).fill("python , postgres");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  // Visibly renamed: the person sees what was saved. That is the reason the
  // vocabulary may invent nothing and refuse nothing — one sees what it does.
  await expect(candidate.getByLabel(/Fähigkeiten/i)).toHaveValue(/PostgreSQL/);

  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(title);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  // Two of three — and the second tick is the one that would not exist without
  // the vocabulary.
  await expect(candidate.getByText(/2 von 3 genannten Fähigkeiten/)).toBeVisible();
  // No percentage, anywhere — and the name of the missing one stands there. The
  // cell carries a cross beside it and "(fehlt dir)"; the name is checked,
  // because that is the promise: a tick says WHICH skill is missing, where a
  // number would hide exactly that. The count is checked too, otherwise a
  // second missing skill would slip through here unnoticed.
  await expect(candidate.locator('[data-match="missing"]')).toHaveCount(1);
  await expect(candidate.locator('[data-match="missing"]')).toContainText("Go");
  await expect(candidate.getByText(/%/)).toHaveCount(0);

  // The same check stood here a second time, on `/jobs/{id}/apply`. That
  // address no longer exists (PBI-6): it was last a switch onto the draft and
  // did not show the tick list anyway. The check above covers the promise
  // completely, and one that checks a vanished view covers nothing at all.

  await recruiterContext.close();
  await anonymousContext.close();
  await candidateContext.close();
});

// The company agent in the browser — and what is checked is the state that
// REALLY holds in the local stack: **no key set**.
//
// That is exactly why this journey is worth something. The case "feature is
// off" is the most dangerous, because it could be silent: a button that is
// pressed and does nothing looks like a button that is loading. The unit test
// checks it against an invented client; here it runs through jobs-service,
// through the `NullDrafter` and back as a real 503.
test("without a provider the writing aid says so — instead of silently doing nothing", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Draft ${Date.now()}`;
  const ownText = `Our own text ${Date.now()}.`;

  const context = await browser.newContext();
  const recruiter = await context.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await switchToCompany(recruiter, companyName);

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel(/Beschreibung/i).fill(ownText);

  // The notice stands AT THE BUTTON and names what would go out — not in a
  // privacy policy. Whoever presses has read it.
  await expect(recruiter.getByText(/nichts über Bewerbende/i)).toBeVisible();

  await recruiter.getByRole("button", { name: /Vorschlag holen/i }).click();

  // Honestly off: a message, not a silent failure. The sentence names the
  // cause, not only the state — "no provider set up" is something somebody can
  // change, "not available" would merely be a refusal.
  await expect(recruiter.getByRole("alert")).toContainText(
    /kein Entwurfsanbieter eingerichtet/i
  );
  // And the company's text stands unchanged. A failure that deletes the work
  // would be worse than no feature at all.
  await expect(recruiter.getByLabel(/Beschreibung/i)).toHaveValue(ownText);

  await context.close();
});

/**
 * The radius search. Checkable only here, and for two reasons.
 *
 * First, it needs a real geolocation interface: `navigator.geolocation` does
 * not exist in jsdom, so every unit test works against an invention. Second,
 * exactly here is the seam where something got lost once before — the browser
 * sends `radius_km`, the service reads `radius_km`, and whether the number
 * arrives in between, neither side sees alone.
 *
 * <strong>The position is ROUNDED before it leaves the house</strong>, and this
 * test pins that too: the address line stands in every access log.
 */
test("the radius search filters by distance — and says what it knows nothing about", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Radius ${Date.now()}`;
  const marker = `E2ERadius${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await switchToCompany(recruiter, companyName);

  // Three adverts, three cases: near, far, and one whose place the place table
  // knows nothing about. The third is what is actually checked.
  const places = [
    // The near case carries a POSTCODE and NO place name. That is what the
    // field of its own is checked for: there is nothing else a point could come
    // from — if the field is not read, the advert counts as "place unknown" and
    // drops out.
    { location: "", postcode: "10115", title: `${marker} Near` },
    // Leipzig and not Hamburg: about 150 km from Berlin, so OUTSIDE the
    // twenty-five and INSIDE the two hundred. Hamburg is 255 km away — the
    // largest selectable distance would never have caught it, and the probe
    // would not have checked the radius at all but only proved that the list
    // stays empty.
    { location: "Leipzig", postcode: "", title: `${marker} Far` },
    // Five words of prose, no postcode: the place table knows nothing about it,
    // and plucking "Hof" (a real town) out of it would be guessing (see the
    // place lookup). The text stays GERMAN on purpose — it is input to a German
    // place table, and only in German does it hide a town name.
    { location: "Auf dem Hof meiner Oma", postcode: "", title: `${marker} Unknown` },
  ];

  for (const { location, postcode, title } of places) {
    await recruiter.goto("/company/jobs/new");
    await recruiter.getByLabel("Titel").fill(title);
    await recruiter.getByLabel(/Beschreibung/i).fill(`What there is to do. ${marker}`);
    await recruiter.getByLabel("Ort", { exact: true }).fill(location);
    await recruiter.getByLabel(/^PLZ$/i).fill(postcode);
    await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();

    const row = recruiter.locator("li").filter({ hasText: title });
    await expect(row).toBeVisible();
    await row.getByRole("button", { name: /Veröffentlichen/i }).click();
    await expect(row.getByText("Veröffentlicht")).toBeVisible();
  }

  // An anonymous visitor standing in Berlin. The permission is granted here
  // because a system dialog cannot ask anybody during a test run — what is
  // checked is not the dialog anyway, but what goes out afterwards.
  const visitorContext = await browser.newContext({
    permissions: ["geolocation"],
    geolocation: { latitude: 52.5170365, longitude: 13.3888599 },
  });
  const visitor = await visitorContext.newPage();

  const requests: string[] = [];
  visitor.on("request", (request) => {
    if (request.url().includes("/jobs?")) requests.push(request.url());
  });

  await visitor.goto("/jobs");
  await visitor.getByLabel(/Suchbegriff/i).fill(marker);

  // Without a location the distance cannot be chosen — a field one can operate
  // that then does nothing would be worse than a disabled one.
  await expect(visitor.getByLabel(/Entfernung/i)).toHaveAttribute("aria-disabled", "true");

  await visitor.getByRole("button", { name: /Meinen Standort verwenden/i }).click();
  await expect(visitor.getByLabel(/Entfernung/i)).not.toHaveAttribute("aria-disabled", "true");

  await pickOption(visitor, /Entfernung/i, "25 km");
  await visitor.getByRole("button", { name: /^Suchen$/i }).click();

  await expect(visitor.getByText(`${marker} Near`)).toBeVisible();
  await expect(visitor.getByText(`${marker} Far`)).toHaveCount(0);
  await expect(visitor.getByText(`${marker} Unknown`)).toHaveCount(0);

  // And it SAYS that it could say nothing about one advert. Without this
  // sentence the result would look complete and would not be.
  await expect(
    visitor.getByText(/nennt keinen Ort, den wir kennen|nennen keinen Ort, den wir kennen/)
  ).toBeVisible();

  // The raw location value must stand in no address line — rounded to two
  // places that is a good kilometre, and anything finer could change no answer,
  // because adverts name towns.
  const withRadius = requests.filter((url) => url.includes("radius_km"));
  expect(withRadius.length).toBeGreaterThan(0);
  for (const url of withRadius) {
    expect(url).toContain("lat=52.52");
    expect(url).toContain("lon=13.39");
    expect(url).not.toContain("52.517");
    expect(url).not.toContain("13.388");
  }

  // Two hundred kilometres bring Leipzig in — otherwise it would not be proved
  // that the radius is read at all. The advert without a known place stays out
  // even then: unknown does not mean "far away", and no radius in the world
  // turns it into a statement.
  await pickOption(visitor, /Entfernung/i, "200 km");
  await visitor.getByRole("button", { name: /^Suchen$/i }).click();
  await expect(visitor.getByText(`${marker} Far`)).toBeVisible();
  await expect(visitor.getByText(`${marker} Unknown`)).toHaveCount(0);

  await recruiterContext.close();
  await visitorContext.close();
});
