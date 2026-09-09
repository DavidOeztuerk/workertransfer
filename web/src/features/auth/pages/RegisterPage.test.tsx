import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { renderMitStore } from "../test/render";
import { RegisterPage } from "./RegisterPage";

type Antwort = { status?: number; body?: unknown; wirft?: boolean };

function antworten(...folge: Antwort[]) {
  const asked: { path: string; body: string }[] = [];
  let number = 0;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      asked.push({
        path: new URL(url).pathname,
        body: String(init?.body ?? ""),
      });
      const answer = folge[number] ??
        folge[folge.length - 1] ?? { status: 201 };
      number += 1;
      if (answer.wirft === true) throw new TypeError("Failed to fetch");
      return new Response(JSON.stringify(answer.body ?? {}), {
        status: answer.status ?? 201,
      });
    }),
  );
  return asked;
}

async function fuelleUndSende(
  user: ReturnType<typeof userEvent.setup>,
  email: string,
) {
  await user.type(screen.getByLabelText(/E-Mail/i), email);
  await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
  await user.type(screen.getByLabelText(/Anzeigename/i), "Max");
  await user.click(screen.getByRole("button", { name: /Registrieren/i }));
}

beforeEach(() => {
  antworten({ status: 201 });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("RegisterPage", () => {
  it("registriert mit einer privaten Adresse und zeigt den Bestätigungshinweis", async () => {
    // Private Adressen sind der Normalfall, nicht die Ausnahme (ADR-0017).
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "max@gmail.com");

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
  });

  it("fragt weder nach einem Mandanten noch nach einer Firma", () => {
    renderMitStore(<RegisterPage />, { route: "/register" });

    expect(screen.queryByLabelText(/Mandant/i)).toBeNull();
    expect(screen.queryByLabelText("Firma")).toBeNull();
    expect(screen.getByLabelText(/E-Mail/i)).toBeInTheDocument();
  });

  it("schickt display_name in snake_case und nie einen Mandanten", async () => {
    const asked = antworten({ status: 201 });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "a@b.com");

    // Der Draht bindet `display_name`. Mit `displayName` kommt die Eingabe
    // nicht herein — gemessen, und der Fehler sah aus wie ein leeres Formular.
    expect(asked[0]?.body).toContain('"display_name"');
    expect(asked[0]?.body).not.toContain("tenant_id");
  });

  it("schickt Vor- und Nachname in snake_case, wenn sie dastehen", async () => {
    const asked = antworten({ status: 201 });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/E-Mail/i), "a@b.com");
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Max");
    await user.type(screen.getByLabelText(/^Vorname$/i), "Maximilian");
    await user.type(screen.getByLabelText(/^Nachname$/i), "Muster");
    await user.click(screen.getByRole("button", { name: /Registrieren/i }));

    expect(asked[0]?.body).toContain('"given_name":"Maximilian"');
    expect(asked[0]?.body).toContain('"family_name":"Muster"');
  });

  it("sagt bei einer bekannten Adresse dasselbe wie bei einer neuen", async () => {
    // Der Server antwortet auch bei bekannter Adresse 201 — kein
    // Aufzählungskanal. Die Oberfläche darf daraus nichts anderes machen.
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "schon-da@firma.de");

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("RegisterPage — Person oder Unternehmen", () => {
  it("registriert voreingestellt eine Person, ohne nach einem Unternehmen zu fragen", () => {
    renderMitStore(<RegisterPage />, { route: "/register" });

    expect(screen.getByRole("radio", { name: "Für mich" })).toBeChecked();
    expect(screen.queryByLabelText(/Name des Unternehmens/i)).toBeNull();
  });

  it("fragt den Firmennamen nur, wenn ein Unternehmen registriert wird", async () => {
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );

    expect(screen.getByLabelText(/Name des Unternehmens/i)).toBeInTheDocument();
  });

  it("wählt das Unternehmen vor, wenn die Adresse es sagt", () => {
    // Die Hero-Knöpfe der Startseite tragen die Absicht als ?as=company mit.
    renderMitStore(<RegisterPage />, { route: "/register?as=company" });

    expect(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    ).toBeChecked();
    expect(screen.getByLabelText(/Name des Unternehmens/i)).toBeInTheDocument();
  });

  it("sagt sofort, dass ein Massenanbieter kein Unternehmen werden kann", async () => {
    // Der Server lehnt ohnehin ab (422), aber wer es erst zwei Schritte später
    // erfährt, hat zwei Schritte verloren.
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );
    await user.type(screen.getByLabelText(/E-Mail/i), "max@gmail.com");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Ein Unternehmen braucht eine eigene Domain.",
    );
    expect(
      screen.getByRole("button", { name: /Registrieren/i }),
    ).toBeDisabled();
  });

  it("urteilt nicht über eine Adresse, die noch kein @ hat", async () => {
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register?as=company" });

    await user.type(screen.getByLabelText(/E-Mail/i), "ma");

    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("prüft Freemail nur beim Beanspruchen einer Domain, nicht bei einer Person", async () => {
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await user.type(screen.getByLabelText(/E-Mail/i), "max@gmail.com");

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByRole("button", { name: /Registrieren/i })).toBeEnabled();
  });

  it("schickt den Firmennamen, und nie einen Mandanten", async () => {
    const asked = antworten({ status: 201 });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await user.click(
      screen.getByRole("radio", { name: "Für ein Unternehmen" }),
    );
    await user.type(screen.getByLabelText(/E-Mail/i), "chef@firma.de");
    await user.type(
      screen.getByLabelText(/Name des Unternehmens/i),
      "Firma GmbH",
    );
    await user.type(screen.getByLabelText(/Passwort/i), "strongpassword1");
    await user.type(screen.getByLabelText(/Anzeigename/i), "Chef");
    await user.click(screen.getByRole("button", { name: /Registrieren/i }));

    expect(asked[0]?.body).toContain('"company_name":"Firma GmbH"');
    expect(asked[0]?.body).not.toContain("tenant_id");
  });

  it("schickt als Person gar kein company_name", async () => {
    const asked = antworten({ status: 201 });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "mensch@example.com");

    // Kein `"company_name":null` — ein null wäre eine Aussage, die niemand
    // gemacht hat.
    expect(asked[0]?.body).not.toContain("company_name");
  });
});

describe("RegisterPage — die E-Mail erneut anfordern", () => {
  it("sagt es, wenn die E-Mail nicht einmal angefordert werden konnte", async () => {
    // Der Knopf tat bei einem Netzfehler sichtbar NICHTS.
    antworten({ status: 201 }, { wirft: true });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die E-Mail konnte gerade nicht angefordert werden.",
    );
    expect(screen.queryByText(/erneut unterwegs/)).toBeNull();
  });

  /**
   * Der zweite Fehlschlag, der vorher als Erfolg durchging: die Bremse
   * (`/auth/resend-verification`, 3/min) antwortet 429. Alt wurde nur der
   * Transportfehler gefangen — die Zusage „ist unterwegs" stand da, obwohl
   * nichts unterwegs war.
   */
  it("meldet auch einen Nicht-2xx als Fehlschlag, statt Versand zu behaupten", async () => {
    antworten(
      { status: 201 },
      { status: 429, body: { detail: "Zu viele Anfragen" } },
    );
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die E-Mail konnte gerade nicht angefordert werden.",
    );
    expect(screen.queryByText(/erneut unterwegs/)).toBeNull();
  });

  it("bestätigt den Versand, wenn er hinausging", async () => {
    antworten({ status: 201 }, { status: 202 });
    const user = userEvent.setup();
    renderMitStore(<RegisterPage />, { route: "/register" });

    await fuelleUndSende(user, "a@b.com");
    await user.click(
      await screen.findByRole("button", { name: "E-Mail erneut senden" }),
    );

    // role="status" und nicht "alert": eine Bestätigung unterbricht nicht.
    expect(await screen.findByRole("status")).toHaveTextContent(
      "erneut unterwegs",
    );
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
