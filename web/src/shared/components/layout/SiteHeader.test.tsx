import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import type { Session } from "../../../features/auth/types/session";
import type { BerufsfeldWahl } from "../../lib/berufsfelder";
import { SiteHeader } from "./SiteHeader";

/**
 * Die Navigation folgt dem Berufsfeld (ADR-0039). Drei Fälle — der mittlere
 * zeigt, dass GitHub nicht abgewertet wird. Es verbirgt nur; die Route bleibt
 * erreichbar.
 */
describe("SiteHeader und das Berufsfeld", () => {
  function konto(berufsfeld: BerufsfeldWahl): Session {
    return {
      userId: "11111111-1111-1111-1111-111111111111",
      email: "anna@example.com",
      tenantId: null,
      language: "de",
      displayName: "Anna Beispiel",
      givenName: "",
      familyName: "",
      berufsfeld,
    };
  }

  async function kontomenue(berufsfeld: BerufsfeldWahl) {
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, {
      auth: { status: "authenticated", session: konto(berufsfeld) },
    });
    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
  }

  it("zeigt GitHub, solange niemand ein Berufsfeld genannt hat", async () => {
    await kontomenue(null);

    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("zeigt GitHub weiterhin bei it_software", async () => {
    await kontomenue("it_software");

    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("trägt Darstellung und Sprache als EINE Zeile, die zeigt was gilt", async () => {
    // Eine Zeile wie jede andere, die den geltenden Wert nennt.
    await kontomenue(null);

    const zeile = screen.getByRole("menuitem", { name: /Darstellung/ });
    expect(zeile).toHaveTextContent("System");
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("führt eine Ebene tiefer statt ein zweites Overlay zu öffnen", async () => {
    // Dieselbe Fläche tauscht ihren Inhalt statt ein zweites Overlay zu öffnen.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, {
      auth: { status: "authenticated", session: konto(null) },
    });
    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
    await user.click(screen.getByRole("menuitem", { name: /Darstellung/ }));

    expect(screen.queryByRole("menuitem", { name: "Profil" })).not.toBeInTheDocument();
    expect(
      screen.getByRole("menuitemradio", { name: /System/ }),
    ).toBeChecked();

    // Die Wahl schliesst das Menü: die Wirkung ist sofort zu sehen, statt
    // hinter einem Modal zu liegen.
    await user.click(screen.getByRole("menuitemradio", { name: "Dunkel" }));

    expect(screen.queryByRole("menu")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
    expect(screen.getByRole("menuitem", { name: /Darstellung/ })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("stellt „Abmelden“ ans Ende, unter die Vorlieben", async () => {
    await kontomenue(null);

    const eintraege = screen.getAllByRole("menuitem").map((e) => e.textContent ?? "");
    expect(eintraege[eintraege.length - 1]).toContain("Abmelden");
  });

  it("nennt im Kontomenü, WER angemeldet ist", async () => {
    // Auf einer Plattform, die über Freigaben entscheidet, ist „unter welchem
    // Konto tue ich das?" keine Kleinigkeit — am Avatar stehen nur zwei
    // Buchstaben.
    await kontomenue(null);

    expect(screen.getByText("anna@example.com")).toBeInTheDocument();
  });

  it("nennt den abgemeldeten Knopf NICHT „Mein Konto“", async () => {
    // Der Name ist zugleich das Signal, an dem die E2E-Anmeldung ihren Erfolg
    // erkennt — er darf abgemeldet nicht dastehen.
    renderMitStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

    expect(screen.queryByRole("button", { name: "Mein Konto" })).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Einstellungen und Anmeldung" }),
    ).toBeInTheDocument();
  });

  it("gibt es NIRGENDS mehr ein Zahnrad", async () => {
    for (const auth of [
      { status: "authenticated" as const, session: konto(null) },
      { status: "anonymous" as const, session: null },
    ]) {
      const { unmount } = renderMitStore(<SiteHeader />, { auth });

      expect(
        screen.queryByRole("button", { name: /Darstellung und Sprache/i }),
      ).not.toBeInTheDocument();

      unmount();
    }
  });

  it("gibt auch abgemeldeten Besuchern Darstellung und Sprache — an derselben Stelle", async () => {
    // Wer die Sprache wechselt, weil er die Oberfläche nicht lesen kann, muss
    // das vor der Anmeldung tun können.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

    await user.click(screen.getByRole("button", { name: "Einstellungen und Anmeldung" }));

    expect(screen.getByRole("menuitem", { name: "Anmelden" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("bietet einem Konto im Handwerk kein GitHub an", async () => {
    await kontomenue("handwerk");

    expect(
      screen.queryByRole("menuitem", { name: "GitHub" }),
    ).not.toBeInTheDocument();

    // Der Rest des Menüs steht.
    expect(
      screen.getByRole("menuitem", { name: "Lebenslauf" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("menuitem", { name: "Arbeitsproben" }),
    ).toBeInTheDocument();
  });
});
