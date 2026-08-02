import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import { ALL_ON } from "../settings/client";
import { renderWithProviders } from "../test/render";
import { SettingsRoute } from "./settings";

vi.mock("../settings/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../settings/client")>();
  return {
    ...actual,
    getNotificationPreferences: vi.fn(),
    saveNotificationPreferences: vi.fn(),
  };
});

const client = await import("../settings/client");
const getNotificationPreferences = vi.mocked(client.getNotificationPreferences);
const saveNotificationPreferences = vi.mocked(client.saveNotificationPreferences);

function principal(): MeResponse {
  return {
    user_id: "11111111-1111-1111-1111-111111111111",
    email: "anna@example.com",
    tenant_id: null,
    roles: ["user"],
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getNotificationPreferences.mockResolvedValue({ ...ALL_ON });
  saveNotificationPreferences.mockResolvedValue({ ok: true, preferences: { ...ALL_ON } });
});

describe("SettingsRoute", () => {
  it("asks for a login rather than showing settings nobody owns", async () => {
    renderWithProviders(<SettingsRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(getNotificationPreferences).not.toHaveBeenCalled();
  });

  it("shows every kind as on before anyone touched them", async () => {
    renderWithProviders(<SettingsRoute principal={principal()} />);

    const switches = await screen.findAllByRole("switch");
    expect(switches).toHaveLength(4);
    for (const control of switches) {
      expect(control.getAttribute("aria-checked")).toBe("true");
    }
  });

  it("applies a toggle at once — there is no save button", async () => {
    // Die Zusage des Bauteils: `Switch` ist bewusst keine Checkbox, weil eine
    // Checkbox verspricht, die Änderung gelte erst beim Absenden.
    renderWithProviders(<SettingsRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("switch", { name: /Marktstatus sehen möchte/ }));

    await waitFor(() =>
      expect(saveNotificationPreferences).toHaveBeenCalledWith({
        ...ALL_ON,
        market_request: false,
      })
    );
    expect(screen.queryByRole("button", { name: /Speichern/ })).toBeNull();
  });

  it("says what a notification will and will not contain", async () => {
    // Die Zusage gehört auf die Seite, auf der jemand über Mails entscheidet —
    // nicht in eine Datenschutzerklärung.
    renderWithProviders(<SettingsRoute principal={principal()} />);

    expect(await screen.findByText(/Kein Firmenname, kein Vorgang, keine/)).toBeTruthy();
  });

  it("mentions the throttle, because it is a promise too", async () => {
    renderWithProviders(<SettingsRoute principal={principal()} />);

    expect(await screen.findByText(/Höchstens eine Mail pro Stunde/)).toBeTruthy();
  });

  it("reports a failed save instead of leaving the switch where it was clicked", async () => {
    saveNotificationPreferences.mockResolvedValue({
      ok: false,
      message: "Keine Verbindung zum Server.",
    });
    renderWithProviders(<SettingsRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("switch", { name: /Lebenslauf fragt/ }));

    expect(await screen.findByRole("alert")).toBeTruthy();
    // Der Schalter zeigt weiterhin, was GILT — nicht, was gewollt war.
    await waitFor(() =>
      expect(
        screen.getByRole("switch", { name: /Lebenslauf fragt/ }).getAttribute("aria-checked")
      ).toBe("true")
    );
  });
});
