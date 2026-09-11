// Stellen durch den Browser — mit der Stelle, die nur hier prüfbar ist:
// eine veröffentlichte Ausschreibung findet auch jemand OHNE Konto.

import { expect, test } from "@playwright/test";

import {
  login,
  registerAndConfirm,
  skipWithoutStack,
  switchToCompany,
  uniqueCompanyDomain,
  uniqueEmail,
  waehleImFeld,
} from "./stack";

skipWithoutStack();

test("eine veröffentlichte Stelle findet auch, wer kein Konto hat", async ({ browser }) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Arbeitgeber ${Date.now()}`;
  const title = `E2E Stelle ${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);
  await expect(recruiter.getByRole("button", { name: "Unternehmen" })).toBeVisible();

  // Erst das Unternehmensprofil: ohne es bleibt die Stelle anonym.
  await recruiter.goto("/company/profile");
  await recruiter.getByLabel(/Anzeigename/i).fill(companyName);
  await recruiter.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(recruiter.getByText(/Profil gespeichert/i)).toBeVisible();

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  await recruiter.getByLabel("Ort", { exact: true }).fill("Hamburg");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  // Erst warten, dann klicken: `click()` hat nur das actionTimeout (15 s),
  // `expect(...).toBeVisible()` das großzügigere expect-Budget. Unter Last
  // scheiterte der Test sonst am Klick statt am Prüfgegenstand.
  await expect(row).toBeVisible();
  await expect(row.getByText(/Entwurf/)).toBeVisible();

  // Ein anonymer Besucher: eigener Kontext, kein Cookie, keine Anmeldung.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  // Ein Entwurf ist für die Öffentlichkeit nicht vorhanden.
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  await anonymous.reload();
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(title)).toBeVisible();
  // Und wer sucht, steht daneben — auch für jemanden ohne Konto.
  await expect(anonymous.getByText(companyName, { exact: true })).toBeVisible();

  // Und dieselbe Stelle steht auf der Karriere-Seite des Unternehmens — einer
  // Adresse, die man weitergeben kann, ohne dass der Empfänger ein Konto
  // braucht. Das Kürzel holt der Recruiter; er ist angemeldet.
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

  // „Bewerben" OHNE KONTO — der Weg, den bis PBI-6 die Seite `/jobs/{id}/apply`
  // trug. Sie ist gefallen (sie war eine Weiche mit einem unerreichbaren
  // Formular darunter), und ihr abgemeldeter Zweig ist hierher gewandert: erst
  // die Stelle merken, dann zur Anmeldung. Geprüft wird er hier, weil die
  // Karriereseite die EINZIGE öffentliche Stelle mit diesem Knopf ist und der
  // Umzug ihn sonst ungeprüft ließe — genau die Art Naht, die still reißt.
  await anonymous.getByRole("button", { name: /Bewerben/i }).first().click();
  await expect(anonymous).toHaveURL(/\/login$/);
  await anonymous.goBack();

  // Geschlossen heißt: für die Öffentlichkeit wieder weg.
  await row.getByRole("button", { name: /Schließen/i }).click();
  await expect(row.getByText("Geschlossen")).toBeVisible();

  // Ausdrücklich zurück zur Suche: der anonyme Browser steht gerade auf der
  // Karriere-Seite, und ein reload() lädt diese neu. Dort gibt es kein
  // Suchfeld, und der Test wartete darauf bis zum Zeitlimit.
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText(/nichts gefunden/i)).toBeVisible();

  await recruiterContext.close();
  await anonymousContext.close();
});

// Die Passung. Nur hier prüfbar, weil genau das der Punkt ist: sie entsteht
// im Browser aus zwei Antworten verschiedener Dienste und steht in keiner
// einzigen davon. Kein Endpunkt liefert sie, also kann kein Endpunkttest sie
// zeigen.
test("die Passung sieht die Person — und niemand rechnet sie auf dem Server", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Passung ${Date.now()}`;
  const title = `E2E Passung Stelle ${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await switchToCompany(recruiter, companyName);

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel("Titel").fill(title);
  await recruiter.getByLabel(/Beschreibung/i).fill("Was zu tun ist.");
  // „PostgreSQL" hier, „postgres" gleich im Profil: das Vokabular (ADR-0023)
  // muss beide auf denselben Namen bringen, sonst zeigt der Abgleich der
  // Person eine Lücke, die es nicht gibt.
  await recruiter.getByLabel(/Gesuchte Fähigkeiten/i).fill("Python, PostgreSQL, Go");
  await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();
  const row = recruiter.locator("li").filter({ hasText: title });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Veröffentlichen/i }).click();
  await expect(row.getByText("Veröffentlicht")).toBeVisible();

  // Ohne Konto: die Anforderungen stehen da, aber niemand wird gezählt.
  const anonymousContext = await browser.newContext();
  const anonymous = await anonymousContext.newPage();
  await anonymous.goto("/jobs");
  await anonymous.getByLabel(/Suchbegriff/i).fill(title);
  await anonymous.getByRole("button", { name: /Suchen/i }).click();
  await expect(anonymous.getByText("PostgreSQL")).toBeVisible();
  await expect(anonymous.getByText(/von 3 genannten/)).toHaveCount(0);

  // Mit Profil: die Liste wird abgehakt — und sagt, was fehlt.
  const candidateEmail = uniqueEmail("kandidatin.example");
  const candidateContext = await browser.newContext();
  const candidate = await candidateContext.newPage();
  await registerAndConfirm(candidate, candidateEmail, "E2E Kandidatin");
  await login(candidate, candidateEmail);
  await candidate.goto("/profile");
  await candidate.getByLabel(/Überschrift/i).fill("Backend-Entwicklerin");
  // Klein geschrieben, mit Leerzeichen — und „postgres" statt „PostgreSQL".
  // Beides darf den Abgleich nicht kosten.
  await candidate.getByLabel(/Fähigkeiten/i).fill("python , postgres");
  await candidate.getByRole("button", { name: "Speichern", exact: true }).click();
  await expect(candidate.getByText(/Profil gespeichert/i)).toBeVisible();
  // Sichtbar umbenannt: die Person sieht, was gespeichert wurde. Das ist der
  // Grund, warum das Vokabular nichts erfinden und nichts ablehnen darf — man
  // sieht ja, was es tut.
  await expect(candidate.getByLabel(/Fähigkeiten/i)).toHaveValue(/PostgreSQL/);

  await candidate.goto("/jobs");
  await candidate.getByLabel(/Suchbegriff/i).fill(title);
  await candidate.getByRole("button", { name: /Suchen/i }).click();
  // Zwei von drei — und der zweite Haken ist der, den es ohne das Vokabular
  // nicht gäbe.
  await expect(candidate.getByText(/2 von 3 genannten Fähigkeiten/)).toBeVisible();
  // Keine Prozentzahl, nirgends — und der Name der fehlenden steht da. Die
  // Zelle trägt daneben ein Kreuz und „(fehlt dir)"; geprüft wird der Name,
  // denn das ist die Zusage: ein Haken sagt, WELCHE Fähigkeit fehlt, wo eine
  // Zahl genau das verstecken würde. Die Anzahl steht mit dabei, sonst würde
  // eine zweite fehlende Fähigkeit hier unbemerkt durchgehen.
  await expect(candidate.locator('[data-match="missing"]')).toHaveCount(1);
  await expect(candidate.locator('[data-match="missing"]')).toContainText("Go");
  await expect(candidate.getByText(/%/)).toHaveCount(0);

  // Hier stand dieselbe Prüfung ein zweites Mal, auf `/jobs/{id}/apply`. Diese
  // Adresse gibt es nicht mehr (PBI-6): sie war zuletzt eine Weiche auf den
  // Entwurf und zeigte die Häkchenliste ohnehin nicht. Die Prüfung oben deckt
  // die Zusage vollständig ab, und eine, die eine verschwundene Ansicht prüft,
  // deckt gar nichts ab.

  await recruiterContext.close();
  await anonymousContext.close();
  await candidateContext.close();
});

// Der Unternehmens-Agent im Browser — und geprüft wird der Zustand, der im
// lokalen Stapel WIRKLICH herrscht: **kein Schlüssel gesetzt**.
//
// Genau deshalb ist diese Reise etwas wert. Der Fall „Funktion ist aus" ist der
// gefährlichste, weil er still sein könnte: ein Knopf, der gedrückt wird und
// nichts tut, sieht aus wie ein Knopf, der lädt. Der Unit-Test prüft das gegen
// einen erfundenen Client; hier läuft es durch jobs-service, durch den
// `NullDrafter` und als echtes 503 zurück.
test("ohne Anbieter sagt die Formulierungshilfe es — statt still nichts zu tun", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Entwurf ${Date.now()}`;
  const eigenerText = `Unser eigener Text ${Date.now()}.`;

  const context = await browser.newContext();
  const recruiter = await context.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await switchToCompany(recruiter, companyName);

  await recruiter.goto("/company/jobs/new");
  await recruiter.getByLabel(/Beschreibung/i).fill(eigenerText);

  // Der Hinweis steht AM KNOPF und nennt, was hinausginge — nicht in einer
  // Datenschutzerklärung. Wer drückt, hat es gelesen.
  await expect(recruiter.getByText(/nichts über Bewerbende/i)).toBeVisible();

  await recruiter.getByRole("button", { name: /Vorschlag holen/i }).click();

  // Ehrlich aus: eine Meldung, kein stiller Fehlschlag. Der Satz nennt die
  // Ursache, nicht nur den Zustand — „kein Anbieter eingerichtet" ist etwas,
  // das jemand ändern kann, „nicht verfügbar" wäre bloß eine Absage.
  await expect(recruiter.getByRole("alert")).toContainText(
    /kein Entwurfsanbieter eingerichtet/i
  );
  // Und der Text des Unternehmens steht unverändert da. Ein Fehlschlag, der
  // die Arbeit löscht, wäre schlimmer als gar keine Funktion.
  await expect(recruiter.getByLabel(/Beschreibung/i)).toHaveValue(eigenerText);

  await context.close();
});

/**
 * Die Umkreissuche. Nur hier prüfbar, und zwar aus zwei Gründen.
 *
 * Erstens braucht sie eine echte Ortungsschnittstelle: `navigator.geolocation`
 * gibt es in jsdom nicht, jeder Einheitentest arbeitet also gegen eine
 * Erfindung. Zweitens ist genau hier die Naht, an der schon einmal etwas
 * verlorenging — der Browser schickt `radius_km`, der Dienst liest `radius_km`,
 * und ob die Zahl dazwischen ankommt, sieht keine der beiden Seiten allein.
 *
 * <strong>Die Position wird GERUNDET, bevor sie das Haus verlässt</strong>, und
 * dieser Test hält das mit: die Adresszeile steht in jedem Zugriffsprotokoll.
 */
test("die Umkreissuche filtert nach Entfernung — und sagt, worüber sie nichts weiß", async ({
  browser,
}) => {
  const domain = uniqueCompanyDomain();
  const recruiterEmail = uniqueEmail(domain);
  const companyName = `E2E Umkreis ${Date.now()}`;
  const marke = `E2EUmkreis${Date.now()}`;

  const recruiterContext = await browser.newContext();
  const recruiter = await recruiterContext.newPage();
  await registerAndConfirm(recruiter, recruiterEmail, "E2E Recruiter", companyName);
  await login(recruiter, recruiterEmail);
  await recruiter.goto("/");
  await waehleImFeld(recruiter, /Handeln als/i, companyName);

  // Drei Anzeigen, drei Fälle: nah, fern, und eine, über deren Ort die
  // Ortstabelle nichts weiß. Der dritte ist der eigentliche Prüfgegenstand.
  const orte = [
    // Der nahe Fall trägt eine POSTLEITZAHL und KEINEN Ortsnamen. Das ist der
    // Prüfgegenstand des eigenen Feldes: es gibt nichts anderes, woraus ein
    // Punkt kommen könnte — wird das Feld nicht gelesen, gilt die Anzeige als
    // „Ort unbekannt" und fällt heraus.
    { ort: "", plz: "10115", titel: `${marke} Nah` },
    // Leipzig und nicht Hamburg: rund 150 km von Berlin, also AUSSERHALB der
    // fünfundzwanzig und INNERHALB der zweihundert. Hamburg liegt 255 km
    // entfernt — die grösste wählbare Entfernung hätte es nie eingefangen, und
    // die Probe hätte den Radius gar nicht geprüft, sondern nur bewiesen, dass
    // die Liste leer bleibt.
    { ort: "Leipzig", plz: "", titel: `${marke} Fern` },
    // Fünf Wörter Prosa, keine Postleitzahl: darüber weiss die Ortstabelle
    // nichts, und „Hof" daraus zu pflücken wäre Raten (siehe Ortskunde).
    { ort: "Auf dem Hof meiner Oma", plz: "", titel: `${marke} Unbekannt` },
  ];

  for (const { ort, plz, titel } of orte) {
    await recruiter.goto("/company/jobs/new");
    await recruiter.getByLabel("Titel").fill(titel);
    await recruiter.getByLabel(/Beschreibung/i).fill(`Was zu tun ist. ${marke}`);
    await recruiter.getByLabel("Ort", { exact: true }).fill(ort);
    await recruiter.getByLabel(/^PLZ$/i).fill(plz);
    await recruiter.getByRole("button", { name: /Entwurf anlegen/i }).click();

    const zeile = recruiter.locator("li").filter({ hasText: titel });
    await expect(zeile).toBeVisible();
    await zeile.getByRole("button", { name: /Veröffentlichen/i }).click();
    await expect(zeile.getByText("Veröffentlicht")).toBeVisible();
  }

  // Ein anonymer Besucher, der in Berlin steht. Die Erlaubnis wird hier
  // erteilt, weil ein Systemdialog im Testlauf niemanden fragen kann — was
  // geprüft wird, ist ohnehin nicht der Dialog, sondern was danach hinausgeht.
  const besucherKontext = await browser.newContext({
    permissions: ["geolocation"],
    geolocation: { latitude: 52.5170365, longitude: 13.3888599 },
  });
  const besucher = await besucherKontext.newPage();

  const anfragen: string[] = [];
  besucher.on("request", (anfrage) => {
    if (anfrage.url().includes("/jobs?")) anfragen.push(anfrage.url());
  });

  await besucher.goto("/jobs");
  await besucher.getByLabel(/Suchbegriff/i).fill(marke);

  // Ohne Standort ist die Entfernung nicht wählbar — ein Feld, das man bedienen
  // kann und das dann nichts tut, wäre schlimmer als ein abgeschaltetes.
  await expect(besucher.getByLabel(/Entfernung/i)).toHaveAttribute("aria-disabled", "true");

  await besucher.getByRole("button", { name: /Meinen Standort verwenden/i }).click();
  await expect(besucher.getByLabel(/Entfernung/i)).not.toHaveAttribute("aria-disabled", "true");

  await waehleImFeld(besucher, /Entfernung/i, "25 km");
  await besucher.getByRole("button", { name: /^Suchen$/i }).click();

  await expect(besucher.getByText(`${marke} Nah`)).toBeVisible();
  await expect(besucher.getByText(`${marke} Fern`)).toHaveCount(0);
  await expect(besucher.getByText(`${marke} Unbekannt`)).toHaveCount(0);

  // Und sie SAGT, dass sie über eine Anzeige nichts sagen konnte. Ohne diesen
  // Satz sähe das Ergebnis vollständig aus und wäre es nicht.
  await expect(
    besucher.getByText(/nennt keinen Ort, den wir kennen|nennen keinen Ort, den wir kennen/)
  ).toBeVisible();

  // Der rohe Ortungswert darf in keiner Adresszeile stehen — gerundet auf zwei
  // Stellen sind das gut ein Kilometer, und feiner könnte an keiner Antwort
  // etwas ändern, weil Anzeigen Städte nennen.
  const mitUmkreis = anfragen.filter((adresse) => adresse.includes("radius_km"));
  expect(mitUmkreis.length).toBeGreaterThan(0);
  for (const adresse of mitUmkreis) {
    expect(adresse).toContain("lat=52.52");
    expect(adresse).toContain("lon=13.39");
    expect(adresse).not.toContain("52.517");
    expect(adresse).not.toContain("13.388");
  }

  // Zweihundert Kilometer holen Leipzig dazu — sonst wäre nicht bewiesen, dass
  // der Radius überhaupt gelesen wird. Die Anzeige ohne bekannten Ort bleibt
  // auch dann draussen: unbekannt heisst nicht „weit weg", und kein Radius der
  // Welt macht daraus eine Aussage.
  await waehleImFeld(besucher, /Entfernung/i, "200 km");
  await besucher.getByRole("button", { name: /^Suchen$/i }).click();
  await expect(besucher.getByText(`${marke} Fern`)).toBeVisible();
  await expect(besucher.getByText(`${marke} Unbekannt`)).toHaveCount(0);

  await recruiterContext.close();
  await besucherKontext.close();
});
