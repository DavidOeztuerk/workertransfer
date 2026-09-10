import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderMitStore } from "../test/render";
import { OverviewPage } from "./OverviewPage";

const SUBJECT = "11111111-1111-1111-1111-111111111111";
const TENANT = "22222222-2222-2222-2222-222222222222";

interface VorgangZeile {
  status: string;
  requires_release: boolean;
  release_confirmed: boolean;
}

function vorgang(werte: Partial<VorgangZeile> = {}): VorgangZeile {
  return {
    status: "interested",
    requires_release: false,
    release_confirmed: false,
    ...werte,
  };
}

/**
 * Die vier Lesezugriffe, nach Pfad beantwortet.
 *
 * `endsWith` und nicht `includes`: `/transfers` ist ein Teilstring von
 * `/transfers/me`, und mit `includes` bekäme die eigene Liste die Antwort der
 * Firmenliste — ein Test, der dann grün ist, ohne etwas zu belegen.
 */
type Antwort = { status?: number; body?: unknown };

function antworten(karte: Record<string, Antwort>) {
  const asked: string[] = [];
  const stub = vi.fn(async (url: string) => {
    const path = new URL(url).pathname;
    asked.push(path);
    const answer = karte[path] ?? { body: [] };
    return new Response(JSON.stringify(answer.body ?? []), {
      status: answer.status ?? 200,
    });
  });
  vi.stubGlobal("fetch", stub);
  return asked;
}

const ANGEMELDET = {
  status: "authenticated" as const,
  session: { userId: SUBJECT, email: "anna@example.com", tenantId: null, language: "de", displayName: "Anna Beispiel", givenName: "", familyName: "", berufsfeld: null },
};

const MIT_FIRMA = {
  status: "authenticated" as const,
  session: {
    userId: SUBJECT,
    email: "anna@example.com",
    tenantId: TENANT,
    language: "de", displayName: "Anna Beispiel", givenName: "", familyName: "", berufsfeld: null,
  },
};

beforeEach(() => {
  antworten({});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("OverviewPage", () => {
  it("sagt klar, wenn gerade nichts wartet", async () => {
    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText("Gerade wartet nichts auf dich."),
    ).toBeInTheDocument();
  });

  it("zählt nur, was auf eine Entscheidung wartet, nicht was von selbst läuft", async () => {
    // Eine Übersicht, die auch anzeigt, was von selbst läuft, ist eine Liste —
    // und Listen übersieht man.
    antworten({
      "/transfers/me": {
        body: [
          vorgang({ status: "interested" }), // wartet auf mich
          vorgang({ status: "talking" }), // läuft, das Unternehmen ist dran
          vorgang({ status: "completed" }), // vorbei
        ],
      },
    });

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText("1 Gespräch wartet auf dich"),
    ).toBeInTheDocument();
  });

  it("zählt eine ausstehende Freigabe, eine bestätigte nicht", async () => {
    antworten({
      "/transfers/me": {
        body: [
          vorgang({ status: "accepted", requires_release: true }),
          vorgang({
            status: "accepted",
            requires_release: true,
            release_confirmed: true,
          }),
        ],
      },
    });

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText("1 Gespräch wartet auf dich"),
    ).toBeInTheDocument();
  });

  it("zählt offene Anfragen beider Arten getrennt", async () => {
    antworten({
      "/market/me/requests": {
        body: [{ status: "PENDING" }, { status: "GRANTED" }],
      },
      "/resumes/me/requests": { body: [{ status: "PENDING" }] },
    });

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText(
        "1 Unternehmen möchte sehen, ob du ansprechbar bist",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText("1 Anfrage nach deinem Lebenslauf"),
    ).toBeInTheDocument();
  });

  it("fragt ohne aktives Unternehmen nicht nach Firmenvorgängen", async () => {
    // Ohne Firma antwortet der Dienst 403 — und ein Fehler, den die Anfrage
    // selbst erzeugt hat, dürfte die Übersicht als unvollständig markieren.
    const asked = antworten({});

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    await screen.findByText("Gerade wartet nichts auf dich.");
    expect(asked).not.toContain("/transfers");
    expect(asked).toContain("/transfers/me");
  });

  it("hält die Sache des Unternehmens von der der Person getrennt", async () => {
    antworten({ "/transfers": { body: [vorgang({ status: "talking" })] } });

    renderMitStore(<OverviewPage />, { auth: MIT_FIRMA });

    expect(
      await screen.findByText("1 Transfer wartet auf euch"),
    ).toBeInTheDocument();
    expect(screen.getByText("Für dein Unternehmen")).toBeInTheDocument();
    expect(screen.queryByText("Für dich")).toBeNull();
  });

  it("gibt ein unvollständiges Bild zu, statt zu behaupten, nichts warte", async () => {
    // „Nichts liegt an" ist die eine Aussage, die nach einer fehlgeschlagenen
    // Abfrage falsch sein kann — und sie wiegt in Sicherheit.
    antworten({
      "/market/me/requests": {
        status: 503,
        body: { detail: "Ledger schweigt" },
      },
    });

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.queryByText("Gerade wartet nichts auf dich.")).toBeNull();
  });

  it("benutzt Singular und Plural, weil ein '1 Gespräche' nach kaputt aussieht", async () => {
    antworten({
      "/transfers/me": {
        body: [
          vorgang({ status: "interested" }),
          vorgang({ status: "offered" }),
        ],
      },
    });

    renderMitStore(<OverviewPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByText("2 Gespräche warten auf dich"),
    ).toBeInTheDocument();
  });

  /**
   * Abgemeldet ist „nichts wartet auf dich" keine leere Übersicht, sondern eine
   * falsche Auskunft: sie sagt nichts über die Person, sondern über das
   * fehlende Token.
   */
  it("zeigt einer abgemeldeten Besucherin keine leere Übersicht", async () => {
    const asked = antworten({});

    renderMitStore(<OverviewPage />, { auth: { status: "anonymous" } });

    expect(screen.queryByText("Gerade wartet nichts auf dich.")).toBeNull();
    expect(screen.queryByRole("heading", { name: "Was liegt an" })).toBeNull();
    expect(asked).toHaveLength(0);
  });
});
