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
    draht.setze("GET /consent/me", {
      body: [granted("profile.visibility:public")],
    });
    renderMitStore(<ConsentsPage />, { auth: ANGEMELDET });

    expect(await screen.findByText(/Profil · Alle Unternehmen/)).toBeTruthy();
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
      body: [granted("profile.visibility:public")],
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
      capability: "profile.visibility:public",
      reason: expect.stringContaining("Freigaben"),
    });
  });

  it("meldet einen gescheiterten Widerruf, statt Erfolg vorzutäuschen", async () => {
    draht.setze("GET /consent/me", {
      body: [granted("profile.visibility:public")],
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
