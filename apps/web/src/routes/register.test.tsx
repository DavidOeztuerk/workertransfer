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
