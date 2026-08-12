import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { RegisterRoute } from "./register";

describe("RegisterRoute", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
  });

  it("registers with a private address and shows the confirmation hint", async () => {
    // Private Adressen sind der Normalfall, nicht die Ausnahme (ADR-0017).
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 201 })));
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "max@gmail.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "Max");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
  });

  it("asks for no company or tenant id", () => {
    render(<RegisterRoute />);

    expect(screen.queryByLabelText("Mandant-ID")).toBeNull();
    expect(screen.queryByLabelText("Firma")).toBeNull();
    expect(screen.getByLabelText("E-Mail")).toBeInTheDocument();
  });

  it("sends display_name in snake_case and no tenant", async () => {
    // Typisiert, damit der Zugriff auf init.body typprüfbar bleibt.
    const fetchMock = vi.fn(
      async (_url: string, _init?: RequestInit) => new Response("{}", { status: 201 })
    );
    vi.stubGlobal("fetch", fetchMock);
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "A");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    const body = String(fetchMock.mock.calls[0]?.[1]?.body ?? "");
    expect(body).toContain('"display_name"');
    expect(body).not.toContain('"tenant_id"');
  });

  // Der Knopf tat bei einem Netzfehler sichtbar NICHTS: `await
  // resendVerification()` wirft, `setResent(true)` läuft nie, und es erschien
  // weder eine Zusage noch ein Fehler.
  it("says so when the mail could not even be requested", async () => {
    let call = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        call += 1;
        // Erster Aufruf: die Registrierung. Zweiter: das erneute Senden.
        if (call === 1) return new Response("{}", { status: 201 });
        throw new TypeError("Failed to fetch");
      })
    );
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "A");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));
    await user.click(await screen.findByRole("button", { name: "E-Mail erneut senden" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Die E-Mail konnte gerade nicht angefordert werden."
    );
    // Und die Zusage darf gerade NICHT dastehen.
    expect(screen.queryByText(/erneut unterwegs/)).toBeNull();
  });

  it("confirms the resend when it went out", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 201 })));
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "a@b.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "A");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));
    await user.click(await screen.findByRole("button", { name: "E-Mail erneut senden" }));

    // role="status" und nicht "alert": eine Bestätigung unterbricht nicht.
    expect(await screen.findByRole("status")).toHaveTextContent("erneut unterwegs");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("shows the same hint for a known address as for a new one", async () => {
    // Der Server antwortet auch bei bekannter Adresse 201 — kein
    // Enumerationskanal. Die Oberfläche darf daraus nichts anderes machen.
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 201 })));
    render(<RegisterRoute />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("E-Mail"), "schon-da@firma.de");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "S");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    expect(await screen.findByText(/E-Mail geschickt/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("RegisterRoute — Person oder Unternehmen", () => {
  it("registers a person by default, without asking about a company", () => {
    render(<RegisterRoute />);

    expect(screen.getByRole("radio", { name: "Für mich" })).toBeChecked();
    expect(screen.queryByLabelText("Name des Unternehmens")).toBeNull();
  });

  it("asks for the company name only when a company is being registered", async () => {
    const user = userEvent.setup();
    render(<RegisterRoute />);

    await user.click(screen.getByRole("radio", { name: "Für ein Unternehmen" }));

    expect(screen.getByLabelText("Name des Unternehmens")).toBeInTheDocument();
  });

  // Die Hero-Knöpfe der Startseite tragen die Absicht als ?as=company mit.
  // Ohne diese Vorauswahl landet jemand, der „Als Unternehmen entdecken" klickt,
  // im Personenformular — und merkt es erst nach der Bestätigungsmail.
  it("preselects a company when the address says so", () => {
    window.history.replaceState({}, "", "/register?as=company");
    try {
      render(<RegisterRoute />);

      expect(screen.getByRole("radio", { name: "Für ein Unternehmen" })).toBeChecked();
      expect(screen.getByLabelText("Name des Unternehmens")).toBeInTheDocument();
    } finally {
      window.history.replaceState({}, "", "/");
    }
  });

  // Sofort, nicht erst nach der Mail: der Server lehnt ohnehin ab (422), aber
  // wer es erst zwei Schritte später erfährt, hat zwei Schritte verloren.
  it("says right away that a mass provider cannot become a company", async () => {
    const user = userEvent.setup();
    render(<RegisterRoute />);

    await user.click(screen.getByRole("radio", { name: "Für ein Unternehmen" }));
    await user.type(screen.getByLabelText("E-Mail"), "max@gmail.com");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Ein Unternehmen braucht eine eigene Domain."
    );
    expect(screen.getByRole("button", { name: "Registrieren" })).toBeDisabled();
  });

  it("sends the company name, and never a tenant", async () => {
    const fetchMock = vi.fn(
      async (_url: string, _init?: RequestInit) => new Response("{}", { status: 201 })
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    render(<RegisterRoute />);

    await user.click(screen.getByRole("radio", { name: "Für ein Unternehmen" }));
    await user.type(screen.getByLabelText("E-Mail"), "chef@firma.de");
    await user.type(screen.getByLabelText("Name des Unternehmens"), "Firma GmbH");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "Chef");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    const body = String(fetchMock.mock.calls[0]?.[1]?.body ?? "");
    expect(body).toContain('"company_name":"Firma GmbH"');
    expect(body).not.toContain('"tenant_id"');
  });

  it("sends no company_name at all when registering as a person", async () => {
    const fetchMock = vi.fn(
      async (_url: string, _init?: RequestInit) => new Response("{}", { status: 201 })
    );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    render(<RegisterRoute />);

    await user.type(screen.getByLabelText("E-Mail"), "mensch@example.com");
    await user.type(screen.getByLabelText("Passwort"), "strongpassword1");
    await user.type(screen.getByLabelText("Anzeigename"), "M");
    await user.click(screen.getByRole("button", { name: "Registrieren" }));

    // Kein `"company_name":null` — ein null wäre eine Aussage, die niemand
    // gemacht hat.
    expect(String(fetchMock.mock.calls[0]?.[1]?.body ?? "")).not.toContain("company_name");
  });
});
