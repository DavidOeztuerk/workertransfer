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
