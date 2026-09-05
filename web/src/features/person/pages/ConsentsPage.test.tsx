import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ANGEMELDET, PERSON, renderMitStore } from "../test/render";
import { type Netz, netz } from "../test/netz";
import { ConsentsPage } from "./ConsentsPage";

const TENANT = "22222222-2222-2222-2222-222222222222";

function granted(capability: string) {
  return { capability, granted_at: "2026-08-02T10:00:00Z" };
}

let draht: Netz;

beforeEach(() => {
  draht = netz({
    "GET /consent/me": { body: [] },
    "POST /consent/revoke": { body: { granted: false } },
    "POST /consent/grant": { body: { granted: true } },
    // Die Seite fragt jetzt auch, ob ein GitHub-Konto verbunden ist — ohne
    // eines gibt es dort nichts freizugeben.
    "GET /github/me": { status: 404 },
    [`GET /companies/${TENANT}/profile`]: { status: 404 },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ConsentsPage", () => {
  it("fragt nach der Anmeldung, statt Freigaben zu zeigen, die niemandem gehören", async () => {
    renderMitStore(<ConsentsPage />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(draht.aufrufe).toHaveLength(0);
  });

  it("zeichnet einen Ladezustand, solange die Sitzung unbekannt ist", async () => {
    // `unknown` heisst „noch nicht gefragt". Als „abgemeldet" gezeichnet, sähe
    // eine angemeldete Person beim Kaltstart „Bitte anmelden".
    renderMitStore(<ConsentsPage />, { auth: { status: "unknown" } });

    expect(await screen.findByRole("status")).toBeTruthy();
    expect(screen.queryByRole("link", { name: "anmelden" })).toBeNull();
  });

  it("sagt deutlich, dass nichts freigegeben ist, wenn nichts freigegeben ist", async () => {
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByText(/Niemand sieht etwas von dir/)).toBeTruthy();
  });

  it("liest die eigene Liste ohne Subjektkennung — weder im Pfad noch in der Abfrage", async () => {
    // Eine fremde Liste würde sagen, welche ANDEREN Unternehmen Zugriff halten.
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    await screen.findByText(/Niemand sieht etwas von dir/);
    const gelesen = draht.letzter("GET /consent/me");
    expect(gelesen?.url).toBe("/consent/me");
    expect(gelesen?.url).not.toContain(PERSON.userId);
  });

  it("zeigt einen Fehler nie als leere Liste", async () => {
    // „Du hast nichts freigegeben" wäre hier die beruhigendste falsche Antwort,
    // die dieses System geben kann.
    draht.setze("GET /consent/me", {
      status: 503,
      body: { title: "Ledger schweigt" },
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText(/Niemand sieht etwas von dir/)).toBeNull();
  });

  it("benennt den Bereich und sagt, dass eine öffentliche Freigabe allen gilt", async () => {
    // `market` statt `profile`: die drei globalen Sichtbarkeiten sind oben
    // Schalter geworden und stehen deshalb nicht mehr als Zeile in der Liste.
    // Der Prüfgegenstand ist unverändert — wie eine öffentliche Freigabe
    // BENANNT wird.
    draht.setze("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByText(/Marktstatus · Alle Unternehmen/)).toBeTruthy();
  });

  it("löst den Firmennamen auf, statt eine UUID zu zeigen", async () => {
    draht.setze("GET /consent/me", {
      body: [granted(`resume.visibility:tenant:${TENANT}`)],
    });
    draht.setze(`GET /companies/${TENANT}/profile`, {
      body: { tenant_id: TENANT, display_name: "Acme GmbH" },
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByText(/Lebenslauf · Acme GmbH/)).toBeTruthy();
    expect(screen.queryByText(new RegExp(TENANT))).toBeNull();
  });

  it("erfindet keinen Namen für ein Unternehmen ohne Profil", async () => {
    draht.setze("GET /consent/me", {
      body: [granted(`market.visibility:tenant:${TENANT}`)],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText(/Marktstatus · Ein Unternehmen/),
    ).toBeTruthy();
  });

  it("zeigt eine unbekannte Capability, statt sie zu verschlucken", async () => {
    // Eine Freigabe zu verbergen, weil die Oberfläche ihr Format nicht kennt,
    // wäre auf genau dieser Seite der schlimmste denkbare Fehler.
    draht.setze("GET /consent/me", {
      body: [granted("something.entirely:new")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByText(/something.entirely:new/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Zurückziehen" })).toBeTruthy();
  });

  it("zieht mit einer Begründung zurück, weil ein Widerruf erklärbar sein muss", async () => {
    draht.setze("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Zurückziehen" }),
    );

    await waitFor(() =>
      expect(draht.letzter("POST /consent/revoke")).toBeDefined(),
    );
    expect(draht.letzter("POST /consent/revoke")?.body).toEqual({
      subject_id: PERSON.userId,
      capability: "market.visibility:public",
      reason: expect.stringContaining("Freigaben"),
    });
  });

  /**
   * <strong>Die Seite kann jetzt ERTEILEN.</strong>
   *
   * Vorher war sie eine Einbahnstrasse: sie zeigte, was gilt, und bot
   * „Zurückziehen". Wer sichtbar werden wollte, musste raten, auf welcher von
   * fünf Seiten der Schalter liegt — eine Seite namens „Meine Freigaben", die
   * keine Freigabe erteilen kann, beantwortet die Frage nicht, für die man sie
   * öffnet.
   */
  it("erteilt eine Freigabe, statt nur zurückziehen zu können", async () => {
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    const schalter = await screen.findByRole("switch", {
      name: /Profil freigeben/,
    });
    expect(schalter).not.toBeChecked();

    await user.click(schalter);

    await waitFor(() =>
      expect(draht.letzter("POST /consent/grant")).toBeDefined(),
    );
    // Beim Erteilen fragt niemand nach einem Grund — nur eine Entziehung muss
    // erklärbar sein (ADR-0027).
    expect(draht.letzter("POST /consent/grant")?.body).toEqual({
      subject_id: PERSON.userId,
      capability: "profile.visibility:public",
    });
  });

  it("zeigt den Schalter als an, wenn die Freigabe gilt — und zieht sie mit Grund zurück", async () => {
    draht.setze("GET /consent/me", {
      body: [granted("portfolio.visibility:public")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    const schalter = await screen.findByRole("switch", {
      name: /Meine Arbeiten freigeben/,
    });
    expect(schalter).toBeChecked();

    await user.click(schalter);

    await waitFor(() =>
      expect(draht.letzter("POST /consent/revoke")).toBeDefined(),
    );
    expect(draht.letzter("POST /consent/revoke")?.body).toMatchObject({
      capability: "portfolio.visibility:public",
      reason: expect.stringContaining("Freigaben"),
    });
  });

  /**
   * Dieselbe Freigabe darf nicht zweimal auf der Seite stehen.
   *
   * Einmal als Schalter und einmal als Zeile mit „Zurückziehen" — und niemand
   * wüsste, welche der beiden gilt.
   */
  it("führt eine geschaltete Freigabe nicht zusätzlich als Zeile", async () => {
    draht.setze("GET /consent/me", {
      body: [granted("profile.visibility:public")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    await screen.findByRole("switch", { name: /Profil freigeben/ });

    expect(screen.queryByText(/Profil · Alle Unternehmen/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Zurückziehen" })).toBeNull();
  });

  /**
   * Ohne verbundenes Konto gibt es bei GitHub nichts freizugeben — der
   * Schalter bleibt trotzdem sichtbar und sagt, was fehlt.
   *
   * Ihn zu verstecken hiesse, die Möglichkeit zu verschweigen. Vor dieser Seite
   * gab es den Schalter überhaupt nicht: der Dienst prüft
   * `github.visibility:public` seit jeher, erteilen konnte man sie nirgends.
   */
  it("sagt bei GitHub, dass zuerst ein Konto verbunden werden muss", async () => {
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    const schalter = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });

    expect(schalter).toBeDisabled();
    expect(screen.getByText(/zuerst ein GitHub-Konto verbinden/)).toBeTruthy();
  });

  /**
   * Ein GENANNTES Konto ist noch kein Beleg.
   *
   * Gemessen an der laufenden Anlage: die Verbindung stand als
   * <c>verified: false</c> da, und der Schalter war bedienbar. Freigegeben
   * wären damit die Repositories eines Kontos, das der Person niemand
   * zugeordnet hat.
   */
  it("lässt eine unbestätigte GitHub-Verbindung nicht freigeben", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "octocat",
        verified: false,
        challenge_description: "workertransfer-verify-abc",
        fetched_at: null,
        repositories: [],
      },
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    const schalter = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });

    expect(schalter).toBeDisabled();
    expect(screen.getByText(/noch nicht nachgewiesen/)).toBeTruthy();
  });

  it("lässt GitHub freigeben, sobald ein Konto verbunden ist", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [],
      },
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    const schalter = await screen.findByRole("switch", {
      name: /GitHub-Belege freigeben/,
    });
    await waitFor(() => expect(schalter).not.toBeDisabled());

    await user.click(schalter);

    await waitFor(() =>
      expect(draht.letzter("POST /consent/grant")?.body).toMatchObject({
        capability: "github.visibility:public",
      }),
    );
  });

  /**
   * Was KEINEN Schalter hat, steht trotzdem auf der Seite — und zwar BEIDE
   * Wege dorthin.
   *
   * Ohne den ersten Absatz sähe die Seite vollständig aus und wäre es nicht:
   * der Lebenslauf fehlt, und wer ihn sucht, hält das für ein Versehen. Er ist
   * keins (ADR-0020).
   *
   * Ohne den ZWEITEN stand dort nur die Hälfte, und die las sich falsch: wer
   * sich gerade selbst bewerben wollte, fand einen Satz über seinen jetzigen
   * Arbeitgeber und schloss daraus, sein Lebenslauf dürfe nirgends hin. Wer
   * sich bewirbt, schickt ihn mit — das ist der Sinn. Beide Absätze müssen
   * dastehen, sonst erklärt die Seite die Hälfte und verwirrt über den Rest.
   */
  it("erklärt beide Wege zum Lebenslauf — den gefragten und den gesendeten", async () => {
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText(/Wenn ein Unternehmen fragt/),
    ).toBeTruthy();
    expect(
      screen.getByText(/Wenn du dich selbst bewirbst, schickst du ihn mit/),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", { name: /Zu meinem Lebenslauf/ }),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", { name: /Zu meinen Bewerbungen/ }),
    ).toBeTruthy();
  });

  it("meldet einen gescheiterten Widerruf, statt Erfolg vorzutäuschen", async () => {
    draht.setze("GET /consent/me", {
      body: [granted("market.visibility:public")],
    });
    draht.setze("POST /consent/revoke", {
      status: 503,
      body: { title: "Keine Verbindung zum Consent-Ledger." },
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Zurückziehen" }),
    );

    expect(await screen.findByRole("alert")).toBeTruthy();
  });
});
