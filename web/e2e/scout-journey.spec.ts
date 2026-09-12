// Der Scout durch den echten Stapel — und zwar an der Stelle, die ihn von
// seinem Vorgänger unterscheidet.
//
// `GET /candidates` konnte suchen und filtern; was es nicht konnte, war sagen,
// WAS jemandem fehlt. Es suchte mit UND — jeder Treffer erfüllte alles, jedes
// Häkchen wäre gesetzt gewesen, und die Liste hätte nichts ausgesagt. Diese
// Reise fährt genau den Unterschied: zwei gesuchte Worte, eine Person, die
// eines davon genannt hat — und ein Haken, der das benennt.
//
// Und sie fährt die Auflagen mit, die man sonst nur im Quelltext liest: keine
// Zahl auf der Karte, und für jemanden ohne Belege ein SATZ statt einer leeren
// Box (ADR-0022 §3).

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  uniqueCompanyDomain,
  uniqueEmail,
  waehleImFeld,
} from "./stack";

skipWithoutStack();

test("der Haken sagt, WELCHE Fähigkeit fehlt — und nennt keine Zahl", async ({
  browser,
}) => {
  const candidateEmail = uniqueEmail("kandidat.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Scout ${Date.now()}`;
  const stamp = Date.now();

  // Zwei Worte, beide erfunden, damit diese Reise nichts von anderen mitsieht.
  const genannt = `Schweissen${stamp}`;
  const fehlend = `Zerspanen${stamp}`;
  const headline = `E2E Scout-Kandidat ${stamp}`;

  // Die Person nennt EINES der beiden Worte und gibt ihr Profil frei.
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidat");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByLabel(/Fähigkeiten/i).fill(genannt);
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
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // Gesucht wird nach BEIDEN Worten. Unter UND käme niemand zurück; unter ODER
  // kommt diese Person — und die Häkchen sagen, woran es liegt.
  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(`${genannt}, ${fehlend}`);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  const karte = recruiter.locator("li").filter({ hasText: headline });
  await expect(karte).toBeVisible();

  // DER PUNKT DER GANZEN REISE: das genannte Wort mit Haken, das fehlende ohne.
  await expect(karte.getByText(`${genannt} ✓`)).toBeVisible();
  await expect(karte.getByText(`${fehlend} ✗`)).toBeVisible();

  // Und keine Zahl daneben. Nicht „1 von 2", nicht „50 %", nichts.
  await expect(karte.getByText(/1 von 2|50\s*%|Passung|Score/i)).toHaveCount(0);

  // Wer nichts auf GitHub hat, ist nicht schlechter, sondern woanders — und die
  // Karte sagt das, statt eine leere Box zu zeigen (ADR-0022 §3).
  await expect(karte.getByText(/Keine Belege freigegeben/)).toBeVisible();

  // Der abgeloeste Pfad fuehrt nirgendwohin mehr: die Route ist weg, und die
  // Oberflaeche zeigt ihre eigene „nicht gefunden"-Seite statt der Liste.
  await recruiter.goto("/candidates");
  await expect(recruiter.getByText(headline)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});

test("wer weiter weg wohnt, bleibt in der Liste — mit einem Kreuz", async ({
  browser,
}) => {
  // ADR-0041 durch den ganzen Stapel. Der Punkt ist eine Aussage über die
  // LÄNGE der Liste: die Stelle filtert nicht, sie erklärt. Wer draussen liegt,
  // bleibt drin und trägt ein Kreuz — das Unternehmen entscheidet, nicht die
  // Suche, und es sieht warum.
  test.slow();

  const stamp = Date.now();
  const skill = `Ankerwickeln${stamp}`;
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Weg ${stamp}`;

  const nahHeadline = `E2E Nah ${stamp}`;
  const fernHeadline = `E2E Fern ${stamp}`;

  // Zwei Menschen mit derselben Fähigkeit, beide freigegeben — und zwei
  // verschiedenen Aussagen über ihren Weg.
  for (const [ort, stufe, headline] of [
    ["Potsdam", "Bis 50 km", nahHeadline],
    ["München", "Bis 10 km", fernHeadline],
  ] as const) {
    const kontext = await browser.newContext();
    const seite = await kontext.newPage();
    const mail = uniqueEmail("kandidat.example");
    await registerAndConfirm(seite, mail, "E2E Kandidat");
    await login(seite, mail);
    await seite.goto("/profile");
    await seite.getByLabel(/Überschrift/i).fill(headline);
    await seite.getByLabel(/Fähigkeiten/i).fill(skill);
    await seite.getByLabel("Standort", { exact: true }).fill(ort);
    await waehleImFeld(seite, /Wie weit würdest du pendeln/i, stufe);
    await seite.getByRole("button", { name: "Speichern", exact: true }).click();
    await expect(seite.getByText(/Profil gespeichert/i)).toBeVisible();
    await seite.getByRole("switch").click();
    await expect(seite.getByRole("switch")).toBeChecked();
    await kontext.close();
  }

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // Eine Stelle in Berlin, vor Ort.
  await recruiter.goto("/company/jobs/new");
  const titel = `E2E Stelle Berlin ${stamp}`;
  await recruiter.getByLabel("Titel").fill(titel);
  await recruiter.getByLabel(/Beschreibung/i).fill("Vor Ort in Berlin.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Berlin");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  await expect(recruiter.getByText(titel)).toBeVisible();

  await recruiter.goto("/scout");
  await recruiter.getByLabel("Fähigkeiten").fill(skill);
  await recruiter.getByRole("button", { name: "Suchen" }).click();

  // Ohne Stelle: beide da, kein Haken zur Erreichbarkeit.
  await expect(recruiter.locator("li").filter({ hasText: nahHeadline })).toBeVisible();
  await expect(recruiter.locator("li").filter({ hasText: fernHeadline })).toBeVisible();

  // Mit Stelle: BEIDE weiterhin da — einer mit Haken, einer mit Kreuz.
  await waehleImFeld(recruiter, /Gegen welche Stelle/i, titel);

  const nah = recruiter.locator("li").filter({ hasText: nahHeadline });
  const fern = recruiter.locator("li").filter({ hasText: fernHeadline });

  await expect(nah).toBeVisible();
  await expect(fern).toBeVisible();
  await expect(nah.getByText("Erreichbarkeit ✓")).toBeVisible();
  await expect(fern.getByText("Erreichbarkeit ✗")).toBeVisible();

  // Und daneben stehen die AUSSAGEN, nicht die Entfernung.
  await expect(fern.getByText(/sagt: Bis 10 km/)).toBeVisible();
  await expect(fern.getByText(/\d+\s*km entfernt|505/)).toHaveCount(0);

  await recruiterContext.close();
});
