import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import { renderWithProviders } from "../test/render";
import { AccountDeletionRoute } from "./account-deletion";

vi.mock("../account/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../account/client")>();
  return { ...actual, requestErasure: vi.fn() };
});

vi.mock("../auth/companies", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../auth/companies")>();
  return { ...actual, useCompanies: vi.fn() };
});

const client = await import("../account/client");
const requestErasure = vi.mocked(client.requestErasure);
const companiesModule = await import("../auth/companies");
const useCompanies = vi.mocked(companiesModule.useCompanies);

function belongsTo(role: string) {
  useCompanies.mockReturnValue({
    companies: [
      {
        id: "22222222-2222-2222-2222-222222222222",
        name: "Beispiel GmbH",
        domain: "beispiel.example",
        role,
      },
    ],
    isLoading: false,
  });
}

function principal(): MeResponse {
  return {
    user_id: "11111111-1111-1111-1111-111111111111",
    email: "anna@example.com",
    tenant_id: null,
    roles: ["user"],
  };
}

function body(): string {
  return document.body.textContent ?? "";
}

beforeEach(() => {
  vi.clearAllMocks();
  requestErasure.mockResolvedValue({ ok: true });
  useCompanies.mockReturnValue({ companies: [], isLoading: false });
});

describe("AccountDeletionRoute", () => {
  it("asks for a login rather than offering to delete nothing", async () => {
    renderWithProviders(<AccountDeletionRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
  });

  describe("what it says BEFORE anyone clicks", () => {
    // Dieselbe Regel wie beim Entwurfsdienst (ADR-0024): an der Stelle, wo
    // gedrückt wird — nicht in einer Datenschutzerklärung.

    it("names what disappears, by name", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      const text = body();
      for (const thing of ["Profil", "Lebenslauf", "Arbeiten", "Bewerbungen", "Marktstatus"]) {
        expect(text, `„${thing}" muss vor dem Klick dastehen`).toContain(thing);
      }
    });

    it("says it does not happen at once", async () => {
      // Der Aufruf sperrt sofort; gelöscht wird über die Outbox in erzwungener
      // Reihenfolge. Eine Seite, die „fertig" verspricht, wäre unwahr.
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/nicht sofort|dauert|läuft danach/i);
    });

    it("says the person is signed out immediately", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/abgemeldet|Sitzung/i);
    });

    it("says out loud what it means for the company", async () => {
      // Die unbequeme Folge der Voreinstellung, und sie gehört sichtbar
      // hierher: ADR-0027 §3 löscht auch die Bewerbung, über die jemand
      // eingestellt wurde.
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/eingestellt/i);
    });

    it("says it cannot be undone and promises no way back", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/unwiderruflich|nicht rückgängig/i);
      // Geprüft wird die ZUSAGE, nicht das Wort: „Es gibt keinen Papierkorb"
      // ist genau der Satz, den diese Seite sagen soll. Verboten ist, eine
      // Wiederherstellung in Aussicht zu stellen — dreißig Tage „falls Sie es
      // sich anders überlegen" hießen, „gelöscht" bedeutet „vielleicht
      // gelöscht".
      expect(body()).not.toMatch(/wiederherstell|zurückholen|reaktivier|\d+ Tage/i);
    });

    it("promises no exception that does not exist", async () => {
      // In der Voreinstellung bleibt NICHTS stehen (ADR-0027 §3). Ein Satz wie
      // „manches bleibt aus rechtlichen Gründen erhalten" wäre eine Zusage über
      // einen Schalter, der auf AUS steht — und niemand würde sie je prüfen.
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).not.toMatch(/Aufbewahrungspflicht|bleibt erhalten|gesetzlich vorgeschrieben/i);
    });

    it("points to the export without demanding it", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      const link = await screen.findByRole("link", { name: /Meine Daten/i });
      expect(link.getAttribute("href")).toBe("/meine-daten");
      // Kein Zwang zum Export vorher: wer ohne Herunterladen löschen will, darf.
      expect(requestErasure).not.toHaveBeenCalled();
    });
  });

  describe("what it tells someone who runs a company (ADR-0027 §7)", () => {
    it("stays quiet for a person without one", async () => {
      // Die überwiegende Mehrheit hat kein Unternehmen. Ein Absatz über
      // Stellenanzeigen wäre für sie Rauschen — und Rauschen ist genau das,
      // was dazu führt, dass der Rest auch nicht gelesen wird.
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).not.toMatch(/Stellenanzeigen|stillgelegt/i);
    });

    it("warns an admin that the company goes quiet and its ads are withdrawn", async () => {
      belongsTo("admin");
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/Beispiel GmbH/);
      expect(body()).toMatch(/stillgelegt|stumm/i);
      expect(body()).toMatch(/Anzeigen/i);
    });

    it("does not claim the deletion waits for the company", async () => {
      // Ein persönliches Recht darf nicht an einer Organisationsfrage hängen:
      // die Stilllegung zählt NICHT in den Vollständigkeitsnachweis, und die
      // Seite darf das Gegenteil nicht nahelegen.
      belongsTo("admin");
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).not.toMatch(/erst wenn|blockiert|zuerst.*übergeben|musst du.*übergeben/i);
    });

    it("tells a plain member their leaving does not close the company", async () => {
      belongsTo("member");
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);

      expect(body()).toMatch(/Beispiel GmbH/);
      expect(body()).not.toMatch(/stillgelegt|stumm/i);
    });
  });

  describe("the deliberate confirmation", () => {
    it("does not delete on the first click", async () => {
      // Gegen den Fehlklick hilft die bewusste Bestätigung VOR der Tat, nicht
      // das Aufbewahren danach (ADR-0027, „Kein Papierkorb").
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));

      expect(requestErasure).not.toHaveBeenCalled();
    });

    it("restates the consequence before the second click", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));

      expect(await screen.findByRole("alert")).toBeTruthy();
      expect(await screen.findByRole("button", { name: /Ja, endgültig löschen/i })).toBeTruthy();
    });

    it("lets someone step back out — wanting to look is not wanting to delete", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Abbrechen/i }));

      expect(screen.queryByRole("button", { name: /Ja, endgültig löschen/i })).toBeNull();
      expect(requestErasure).not.toHaveBeenCalled();
    });

    it("deletes on the second, differently worded click", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));

      await waitFor(() => expect(requestErasure).toHaveBeenCalledTimes(1));
    });

    it("asks for no reason anywhere on the page", async () => {
      // Von einem Menschen, der sein Konto löschen will, eine Begründung zu
      // verlangen, ist ein Hebel gegen ihn (ADR-0027 §Kontext 5). Geprüft wird
      // deshalb, dass es kein Eingabefeld und keine Frage gibt — nicht, dass
      // das Wort „Grund" nirgends vorkommt: die Seite erklärt durchaus, aus
      // welchem Grund die Löschung dauert.
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();
      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));

      expect(screen.queryByRole("textbox")).toBeNull();
      expect(screen.queryByRole("combobox")).toBeNull();
      expect(body()).not.toMatch(/Warum (möchtest|willst|verlässt) du/i);
    });
  });

  describe("afterwards", () => {
    it("says it is running, not that it is done", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));

      const done = await screen.findByRole("status");
      expect(done.textContent).toMatch(/läuft|angenommen/i);
      expect(done.textContent).not.toMatch(/erledigt|abgeschlossen|fertig/i);
    });

    it("shows no progress bar and no way back in", async () => {
      // Wer sich noch anmelden könnte, um zuzusehen, hätte ein Konto, das noch
      // funktioniert — und genau das soll nicht mehr stimmen (ADR-0027 §6).
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));
      await screen.findByRole("status");

      expect(screen.queryByRole("progressbar")).toBeNull();
      expect(screen.queryByRole("button", { name: /Ja, endgültig löschen/i })).toBeNull();
    });

    it("announces the one message that will follow", async () => {
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));

      expect((await screen.findByRole("status")).textContent).toMatch(/E-Mail|Nachricht/i);
    });

    it("keeps saying it worked after the session is gone", async () => {
      // DER FEHLER, DEN DIE E2E-REISE GEFUNDEN HAT und diese Reihe nicht
      // finden konnte: die Seite räumt bei Erfolg die Sitzung weg, und die
      // Hülle rendert sie danach mit `principal = null` neu. Nahm die
      // Anmeldeaufforderung den Vorrang, sah die Person nach dem Löschen
      // „Bitte anmelden, um dein Konto zu löschen" — als wäre nichts passiert,
      // beim unwiderruflichsten Vorgang des Systems.
      //
      // Die Tests hier haben das nicht gesehen, weil sie `principal` als feste
      // Eigenschaft übergeben. Deshalb wird hier ausdrücklich NEU gerendert.
      const { rerender } = renderWithProviders(
        <AccountDeletionRoute principal={principal()} />
      );
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));
      await screen.findByRole("status");

      rerender(<AccountDeletionRoute principal={null} />);

      expect(await screen.findByRole("status")).toBeTruthy();
      expect(screen.queryByRole("link", { name: "anmelden" })).toBeNull();
    });

    it("drops the session so nothing keeps acting under that name", async () => {
      // Eigener QueryClient mit `gcTime: Infinity`: die Vorlage aus
      // `renderWithProviders` räumt sofort auf, und diese Seite beobachtet die
      // Sitzung nicht selbst — der Eintrag wäre also weggeräumt, bevor der
      // Test ihn ansehen kann, und der Test grün aus dem falschen Grund.
      const queryClient = new QueryClient({
        defaultOptions: {
          queries: { retry: false, gcTime: Infinity },
          mutations: { retry: false },
        },
      });
      renderWithProviders(<AccountDeletionRoute principal={principal()} />, { queryClient });
      queryClient.setQueryData(["session"], principal());
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));
      await screen.findByRole("status");

      await waitFor(() => expect(queryClient.getQueryData(["session"])).toBeNull());
    });
  });

  describe("when it fails", () => {
    it("says nothing was deleted instead of leaving it open", async () => {
      requestErasure.mockResolvedValue({
        ok: false,
        message: "Die Löschung konnte nicht angestoßen werden. Es wurde nichts gelöscht.",
      });
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));

      const alerts = await screen.findAllByRole("alert");
      expect(alerts.some((node) => /nichts gelöscht/i.test(node.textContent ?? ""))).toBe(true);
    });

    it("keeps the way open for a second try", async () => {
      requestErasure.mockResolvedValue({ ok: false, message: "Keine Verbindung zum Server." });
      renderWithProviders(<AccountDeletionRoute principal={principal()} />);
      const user = userEvent.setup();

      await user.click(await screen.findByRole("button", { name: /Konto löschen/i }));
      await user.click(await screen.findByRole("button", { name: /Ja, endgültig löschen/i }));

      expect(await screen.findByRole("button", { name: /Ja, endgültig löschen/i })).toBeTruthy();
    });
  });
});
