import { cleanup, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import { renderMitStore } from "../../work/test/render";
import { CompanyAdvisorPage } from "./CompanyAdvisorPage";

/**
 * Die eine Zusage dieser Seite, als Test.
 *
 * <strong>Was in einer Stufe nicht freigegeben ist, existiert für die
 * Gegenseite nicht</strong> (ADR-0037). Der Server lässt das Feld im JSON ganz
 * weg; diese Seite darf daraus keinen Hinweis basteln — kein „noch nicht
 * freigegeben", kein Schloss, keine graue Zeile. Ein solcher Hinweis verriete
 * genau das, was die Stufe zurückhält: dass es etwas gibt.
 *
 * <strong>Gestubbt wird `fetch`, nicht das Client-Modul.</strong> So laufen
 * Pfad, Methode und snake_case durch den echten Client — ein Feld, das die
 * Seite unter einem anderen Namen liest, käme sonst nie an, und der Test bliebe
 * grün.
 */
function stubFetch(body: unknown) {
  const spion = vi.fn(
    async () =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
  );

  vi.stubGlobal("fetch", spion);
  return spion;
}

const FIRMA = "11111111-1111-1111-1111-111111111111";
const ANNA = "33333333-3333-3333-3333-333333333333";

/** Eine Sitzung, die für ein Unternehmen handelt. */
const alsFirma = {
  status: "authenticated" as const,
  session: {
    userId: "22222222-2222-2222-2222-222222222222",
    email: "werber@beispiel.test",
    displayName: "Werber",
    tenantId: FIRMA,
  } as never,
};

afterEach(() => {
  vi.unstubAllGlobals();
  cleanup();
});

it("zeigt auf Stufe 1 weder Gehalt noch Namen — und keinen Hinweis darauf", async () => {
  // GENAU DIE GESTALT DES SERVERS: die Felder der hoeheren Stufen FEHLEN, sie
  // sind nicht `null`. Ein `null` hier waere eine erfundene Gestalt, und der
  // Test pruefte etwas, das es auf dem Draht nie gibt.
  stubFetch({
    items: [
      {
        id: "44444444-4444-4444-4444-444444444444",
        subject_id: ANNA,
        state: "talking",
        stage: 1,
        note: "Wir suchen jemanden für verteilte Systeme.",
        opened_at: "2026-09-11T10:00:00Z",
        updated_at: "2026-09-11T10:00:00Z",
        entry_month: "2026-11",
        workload_percent: 80,
      },
    ],
  });

  renderMitStore(<CompanyAdvisorPage />, { auth: alsFirma });

  await waitFor(() => expect(screen.getByText(/2026-11/)).toBeInTheDocument());

  // Das Freigegebene steht da.
  expect(screen.getByText(/80/)).toBeInTheDocument();

  // Und das Nichtfreigegebene fehlt GANZ — nicht als leeres Feld, nicht als
  // Hinweis, nicht als Platzhalter.
  expect(screen.queryByText(/€/)).not.toBeInTheDocument();
  expect(screen.queryByText(/Anna/)).not.toBeInTheDocument();
  expect(screen.queryByText(/gesperrt|freigegeben werden|noch nicht/i)).not.toBeInTheDocument();

  // Ohne Klarnamen steht die Kennung da und kein erfundener Platzhalter: ein
  // „anonym" behauptete, die Person habe sich verborgen.
  expect(screen.getByText(ANNA)).toBeInTheDocument();
});

it("zeigt auf Stufe 3 Name, Adresse und Spanne", async () => {
  stubFetch({
    items: [
      {
        id: "44444444-4444-4444-4444-444444444444",
        subject_id: ANNA,
        state: "agreed",
        stage: 3,
        note: "",
        opened_at: "2026-09-11T10:00:00Z",
        updated_at: "2026-09-11T10:00:00Z",
        entry_month: "2026-11",
        workload_percent: 80,
        salary_min: 4000,
        salary_max: 5200,
        name: "Anna Beispiel",
        email: "anna@beispiel.test",
      },
    ],
  });

  renderMitStore(<CompanyAdvisorPage />, { auth: alsFirma });

  await waitFor(() => expect(screen.getByText("Anna Beispiel")).toBeInTheDocument());

  expect(screen.getByText("anna@beispiel.test")).toBeInTheDocument();
  expect(screen.getByText(/4000–5200/)).toBeInTheDocument();

  // Nach der Zustimmung darf uebergeben werden — und nur dann.
  expect(screen.getByRole("button", { name: /Transfer/ })).toBeInTheDocument();
});

it("eine leere Liste ist eine Antwort, kein Fehler", async () => {
  stubFetch({ items: [] });

  renderMitStore(<CompanyAdvisorPage />, { auth: alsFirma });

  await waitFor(() =>
    expect(screen.getByText("Es läuft gerade kein Gespräch.")).toBeInTheDocument(),
  );
});

it("ohne Unternehmen gibt es hier nichts zu sehen", async () => {
  const spion = stubFetch({ items: [] });

  renderMitStore(<CompanyAdvisorPage />, {
    auth: {
      status: "authenticated",
      session: {
        userId: ANNA,
        email: "anna@beispiel.test",
        displayName: "Anna",
        tenantId: null,
      } as never,
    },
  });

  expect(
    screen.getByText(/Gespräche führen nur Unternehmen/),
  ).toBeInTheDocument();

  // Und es wird gar nicht erst gefragt: eine Anfrage, die sicher 403 bekommt,
  // ist eine Fehlermeldung, die niemand braucht.
  expect(spion).not.toHaveBeenCalled();
});
