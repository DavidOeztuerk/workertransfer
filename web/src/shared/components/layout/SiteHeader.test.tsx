import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { renderMitStore } from "../../../features/auth/test/render";
import type { Session } from "../../../features/auth/types/session";
import type { BerufsfeldWahl } from "../../lib/berufsfelder";
import { SiteHeader } from "./SiteHeader";

/**
 * Die Navigation folgt dem Berufsfeld (ADR-0039).
 *
 * <strong>Drei Fälle, und der mittlere ist der wichtigste.</strong> Ohne ihn
 * liesse sich diese Arbeit als Abwertung von GitHub lesen — sie ist keine. Ein
 * Konto mit `it_software` sieht denselben Eintrag wie vorher, und ein Konto
 * ohne Berufsfeld ebenfalls: es wird nur etwas HINZUGEFÜGT.
 *
 * <strong>Es verbirgt, es schützt nicht.</strong> Was hier nicht steht, ist
 * trotzdem erreichbar; die Route `/github` antwortet unverändert. Ein Test, der
 * daraus eine Zugriffsprüfung machte, prüfte die falsche Sache.
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

    // Die Zusage, an der diese Arbeit gemessen wird: wer nichts gewählt hat,
    // sieht die heutige Ansicht. Nichts wird schlechter.
    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("zeigt GitHub weiterhin bei it_software", async () => {
    await kontomenue("it_software");

    // GitHub wird nicht abgewertet — es verliert nur die Eigenschaft, die
    // EINZIGE Belegquelle zu sein.
    expect(
      screen.getByRole("menuitem", { name: "GitHub" }),
    ).toBeInTheDocument();
  });

  it("trägt Darstellung und Sprache als EINE Zeile, die zeigt was gilt", async () => {
    // Angemeldet gab es einmal ein Zahnrad NEBEN dem Nutzersymbol, von dem das
    // eine eine Teilmenge des anderen war. Danach standen dort zwei
    // Auswahlfelder mit Überschrift und Erklärung — ein Menü, das zwei
    // Bedienarten übereinanderstapelte. Jetzt ist es eine Zeile wie jede
    // andere, und sie nennt den geltenden Wert, ohne dass man sie öffnet.
    await kontomenue(null);

    const zeile = screen.getByRole("menuitem", { name: /Darstellung/ });
    expect(zeile).toHaveTextContent("System");
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("führt eine Ebene tiefer statt ein zweites Overlay zu öffnen", async () => {
    // Dieselbe Fläche tauscht ihren Inhalt: das Menü bleibt, wo es war. Ein
    // zweites Menü daneben müsste positioniert werden, liefe an schmalen
    // Fenstern über den Rand und brächte die Tastatursteuerung durcheinander.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, {
      auth: { status: "authenticated", session: konto(null) },
    });
    await user.click(screen.getByRole("button", { name: "Mein Konto" }));
    await user.click(screen.getByRole("menuitem", { name: /Darstellung/ }));

    // Die Wege sind weg, die Möglichkeiten da — eine Ebene, nicht zwei.
    expect(screen.queryByRole("menuitem", { name: "Profil" })).not.toBeInTheDocument();
    expect(
      screen.getByRole("menuitemradio", { name: /System/ }),
    ).toBeChecked();

    await user.click(screen.getByRole("menuitemradio", { name: "Dunkel" }));

    // Und zurück auf der Hauptebene, mit dem neuen Wert in der Zeile.
    expect(screen.getByRole("menuitem", { name: /Darstellung/ })).toHaveTextContent(
      "Dunkel",
    );
  });

  it("stellt „Abmelden“ ans Ende, unter die Vorlieben", async () => {
    // „Abmelden" ist der letzte Eintrag eines Kontomenüs, in jeder Anwendung,
    // die eines hat — was darunter steht, sieht aus wie ein Nachtrag.
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

  it("gibt es NIRGENDS mehr ein Zahnrad", async () => {
    // Es stand abgemeldet neben dem Nutzersymbol und verschwand beim Anmelden.
    // Damit wanderte die Stelle, an der man etwas einstellt — genau die
    // Bewegung, die diese Kopfzeile sonst überall vermeidet.
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
    // Wer die Sprache wechselt, WEIL er die Oberfläche nicht lesen kann, muss
    // das vor der Anmeldung tun können. Und zwar dort, wo er es danach wieder
    // tut: das Nutzersymbol steht in beiden Zuständen an derselben Stelle.
    const user = userEvent.setup();
    renderMitStore(<SiteHeader />, { auth: { status: "anonymous", session: null } });

    await user.click(screen.getByRole("button", { name: "Mein Konto" }));

    expect(screen.getByRole("menuitem", { name: "Anmelden" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /Sprache/ })).toBeInTheDocument();
  });

  it("bietet einem Konto im Handwerk kein GitHub an", async () => {
    await kontomenue("handwerk");

    expect(
      screen.queryByRole("menuitem", { name: "GitHub" }),
    ).not.toBeInTheDocument();

    // Und der Rest des Menüs steht: es wird EIN Eintrag nicht angeboten, nicht
    // das Konto beschnitten.
    expect(
      screen.getByRole("menuitem", { name: "Lebenslauf" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("menuitem", { name: "Arbeitsproben" }),
    ).toBeInTheDocument();
  });
});
