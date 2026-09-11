// Die Arbeitsprobe durch den echten Stapel — an den drei Stellen, an denen sie
// von allem anderen abweicht (ADR-0042).
//
// ERSTENS: der UMFANG steht vorne. Diese Reise liest „4 Stunden" auf der Seite
// der Person und weist eine Aufgabe mit neun Stunden ab — eine Grenze, die nur
// im Server steht, ist eine, die niemand sieht.
//
// ZWEITENS: die PERSON sieht die Bewertung, auch bei einer Absage. Der Weg geht
// deshalb absichtlich durch `rejected` und nicht durch `accepted`: bei einer
// Zusage fiele niemandem auf, wenn die Rückmeldung fehlte.
//
// DRITTENS: es gibt keinen Ablehnen-Knopf, und die Seite sagt das. Diese Reise
// prüft beides — dass der Knopf fehlt und dass der Satz dasteht —, weil ein
// fehlender Knopf ohne Satz wie ein Versehen aussieht.
//
// Der Draht ist der Grund, warum es diese Reise geben muss: `due_at` als
// `dueAt` gebunden käme nie an, und jede Handler- und Komponentenreihe bliebe
// grün.

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

/** Ein Datum in der Zukunft, als `JJJJ-MM-TT` für ein Datumsfeld. */
function inTagen(tage: number): string {
  const ziel = new Date(Date.now() + tage * 24 * 60 * 60 * 1000);

  return ziel.toISOString().slice(0, 10);
}

test("Umfang vorne, Absage mit Begründung, kein Ablehnen-Knopf", async ({ browser }) => {
  const stamp = Date.now();
  const candidateEmail = uniqueEmail("probe.example");
  const companyDomain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(companyDomain);
  const companyName = `E2E Probe ${stamp}`;
  const headline = `E2E Probe-Kandidat ${stamp}`;

  // --- Die Person: Profil und Freigabe ------------------------------------

  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Probandin");
  await login(candidate, candidateEmail);

  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill(headline);
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();

  // Eine Arbeitsprobe steht an derselben Freigabe wie ein Gespraech — an einer
  // BESTEHENDEN Sichtbarkeit, nicht an einer eigenen Faehigkeit.
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).toBeChecked();

  // Die eigene Kennung — dieselbe, die eine Firma aus Scout oder Gespraech
  // bekommt. Ueber den Hafen von identity-service und nicht ueber den
  // Vite-Server: eine relative Adresse traefe den Dev-Server, und der kennt
  // `/me` nicht.
  const subjectId = await candidate.evaluate(async () => {
    const antwort = await fetch(
      `${window.location.origin.replace(":5173", ":8001")}/me`,
      { credentials: "include" }
    );
    return ((await antwort.json()) as { user_id: string }).user_id;
  });

  expect(subjectId).not.toBe("");

  // --- Das Unternehmen stellt eine Aufgabe --------------------------------

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Werber", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  await recruiter.goto("/company/assessments");

  // ZUERST DIE GEGENPROBE: neun Stunden gehen nicht. Mehr als ein Arbeitstag
  // ist keine Probe mehr, sondern Arbeit — und die Seite sagt das, statt es
  // jemanden ausprobieren zu lassen.
  await expect(recruiter.getByText(/Höchstens 8/)).toBeVisible();

  await recruiter.getByLabel("Wem").fill(subjectId);
  await recruiter.getByLabel("Überschrift").fill(`Kleiner Dienst ${stamp}`);
  await recruiter
    .getByLabel("Die Aufgabe")
    .fill("Bau einen kleinen Dienst, der eine Liste ausliefert.");
  await recruiter.getByLabel(/Umfang in Stunden/i).fill("9");
  await recruiter.getByLabel(/Bis wann/i).fill(inTagen(7));
  await recruiter.getByRole("button", { name: "Aufgabe stellen", exact: true }).click();

  await expect(recruiter.getByText(/between 1 and 8 hours/i)).toBeVisible();

  // Und jetzt richtig.
  await recruiter.getByLabel(/Umfang in Stunden/i).fill("4");
  await recruiter.getByRole("button", { name: "Aufgabe stellen", exact: true }).click();

  const firmenkarte = recruiter.locator(".MuiCard-root").filter({
    hasText: `Kleiner Dienst ${stamp}`,
  });
  await expect(firmenkarte).toBeVisible();

  // --- Die Person sieht den Umfang, und keinen Ablehnen-Knopf -------------

  await candidate.goto("/assessments");

  const karte = candidate.locator(".MuiCard-root").filter({
    hasText: `Kleiner Dienst ${stamp}`,
  });
  await expect(karte).toBeVisible();

  // DER UMFANG, auf der Seite und nicht im Kleingedruckten.
  await expect(karte.getByText("4 Stunden")).toBeVisible();

  // KEIN ABLEHNEN-KNOPF — und der Satz, der sagt, dass das Absicht ist.
  await expect(candidate.getByText(/es gibt keinen Absageknopf/i)).toBeVisible();
  await expect(candidate.getByRole("button", { name: /Ablehnen|Absagen/i })).toHaveCount(0);

  // --- Abgeben ------------------------------------------------------------

  await karte.getByLabel("Deine Lösung").fill("Liegt im Repository.");
  await karte.getByLabel(/^Link/).fill("https://beispiel.test/loesung");
  await karte.getByRole("button", { name: "Abgeben", exact: true }).click();

  // `exact`, weil „Abgegeben" sonst auch die Ueberschrift „Was du abgegeben
  // hast" traefe — zwei Treffer sind in Playwright ein Fehler, und ein
  // unscharfer Selektor hat hier schon einmal vierzehn Reisen lahmgelegt.
  await expect(karte.getByText("Abgegeben", { exact: true })).toBeVisible();

  // --- Eine ABSAGE, und sie braucht einen Text ----------------------------

  await recruiter.reload();
  await expect(firmenkarte.getByText("https://beispiel.test/loesung")).toBeVisible();

  await firmenkarte.getByRole("radio", { name: /Es geht nicht weiter/ }).check();

  // DIE ZUSAGE: ohne Text ist der Knopf gesperrt. Ablehnen und Begruenden sind
  // ein Schritt und nicht zwei.
  await expect(firmenkarte.getByRole("button", { name: /Rückmeldung senden/i })).toBeDisabled();

  await firmenkarte
    .getByLabel("Deine Rückmeldung")
    .fill("Sauber gelöst, aber die Fehlerbehandlung fehlt.");
  await firmenkarte.getByRole("button", { name: /Rückmeldung senden/i }).click();

  // --- Und die Person liest sie — obwohl es eine Absage war ---------------

  await candidate.goto("/assessments");
  await expect(karte.getByText("Es geht nicht weiter.")).toBeVisible();
  await expect(
    karte.getByText(/Sauber gelöst, aber die Fehlerbehandlung fehlt\./)
  ).toBeVisible();

  // --- Sie liest sie auch nach einem Widerruf -----------------------------
  //
  // DER SCHÄRFSTE FALL: wer seine Sichtbarkeit zurücknimmt, verschwindet für
  // das Unternehmen — aber nicht aus seiner eigenen Bewertung. Eine
  // Beurteilung, aus der man sich aussperren kann, wäre eine, die der
  // Beurteilte nie liest.

  await candidate.goto("/profile");
  await candidate.getByRole("switch").click();
  await expect(candidate.getByRole("switch")).not.toBeChecked();

  await candidate.goto("/assessments");
  await expect(
    karte.getByText(/Sauber gelöst, aber die Fehlerbehandlung fehlt\./)
  ).toBeVisible();

  await recruiter.reload();
  await expect(recruiter.getByText(`Kleiner Dienst ${stamp}`)).toHaveCount(0);

  await candidateContext.close();
  await recruiterContext.close();
});
