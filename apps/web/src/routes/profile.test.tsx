import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
// Typen separat: `client` unten ist ein Wert aus await import(), kein
// Namespace — `client.Profile` als Typ wäre TS2503.
import type { Profile } from "../profile/client";
import { renderWithProviders } from "../test/render";
import { ProfileRoute } from "./profile";

vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return {
    ...actual,
    getMyProfile: vi.fn(),
    saveMyProfile: vi.fn(),
    getVisibility: vi.fn(),
    setVisibility: vi.fn(),
    draftProfileText: vi.fn(),
  };
});

const client = await import("../profile/client");
const getMyProfile = vi.mocked(client.getMyProfile);
const saveMyProfile = vi.mocked(client.saveMyProfile);
const getVisibility = vi.mocked(client.getVisibility);
const setVisibility = vi.mocked(client.setVisibility);
const draftProfileText = vi.mocked(client.draftProfileText);

const SUBJECT = "11111111-1111-1111-1111-111111111111";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function profile(overrides: Partial<Profile> = {}): Profile {
  return {
    subject_id: SUBJECT,
    headline: "Senior Python",
    bio: "Zehn Jahre Backend.",
    location: "Berlin",
    remote_ok: true,
    skills: ["Python", "FastAPI"],
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyProfile.mockResolvedValue(null);
  getVisibility.mockResolvedValue(false);
  saveMyProfile.mockResolvedValue({ ok: true, profile: profile() });
  setVisibility.mockResolvedValue({ ok: true, granted: true });
  draftProfileText.mockResolvedValue({ ok: true, draft: "Ich baue Backends." });
});

describe("ProfileRoute", () => {
  it("offers an empty form when nothing has been saved yet", async () => {
    renderWithProviders(<ProfileRoute principal={principal()} />);

    const headline = await screen.findByLabelText(/Überschrift/i);
    expect(headline).toHaveValue("");
  });

  it("fills the form with what is already stored", async () => {
    getMyProfile.mockResolvedValue(profile());

    renderWithProviders(<ProfileRoute principal={principal()} />);

    expect(await screen.findByLabelText(/Überschrift/i)).toHaveValue("Senior Python");
    expect(screen.getByLabelText(/Ort/i)).toHaveValue("Berlin");
    expect(screen.getByLabelText(/Fähigkeiten/i)).toHaveValue("Python, FastAPI");
  });

  it("splits the skills field on commas and drops the blanks", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await user.type(await screen.findByLabelText(/Überschrift/i), "Senior Python");
    await user.type(screen.getByLabelText(/Fähigkeiten/i), "Python, , FastAPI ,");
    await user.click(screen.getByRole("button", { name: /Speichern/i }));

    await waitFor(() => expect(saveMyProfile).toHaveBeenCalledTimes(1));
    expect(saveMyProfile.mock.calls[0]?.[0].skills).toEqual(["Python", "FastAPI"]);
  });

  it("keeps a rejected form on screen and does not claim success", async () => {
    const user = userEvent.setup();
    saveMyProfile.mockResolvedValue({
      ok: false,
      reason: "invalid",
      message: "Headline exceeds 120 characters",
    });
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await user.type(await screen.findByLabelText(/Überschrift/i), "zu lang");
    await user.click(screen.getByRole("button", { name: /Speichern/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Headline exceeds 120 characters");
    // Buchstabengetreu, nicht /gespeichert/i: der lockere Ausdruck traf auch
    // den Hinweis der Formulierungshilfe („Gespeichert wird nichts"), und der
    // Test wäre dann rot geworden, ohne dass etwas kaputt war. Dieselbe Lehre
    // wie beim E2E-Wackler, den /bestätigt/i verursacht hat: eine
    // Erfolgsmeldung prüft man wörtlich.
    expect(screen.queryByText("Profil gespeichert.")).toBeNull();
  });

  it("shows the release state read from the ledger, not from the profile", async () => {
    getMyProfile.mockResolvedValue(profile());
    getVisibility.mockResolvedValue(true);

    renderWithProviders(<ProfileRoute principal={principal()} />);

    await waitFor(() => expect(screen.getByRole("switch")).toBeChecked());
    expect(getVisibility).toHaveBeenCalledWith(SUBJECT);
  });

  it("writes a release to the ledger only — never through the profile", async () => {
    const user = userEvent.setup();
    getMyProfile.mockResolvedValue(profile());
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await waitFor(() => expect(screen.getByRole("switch")).not.toBeChecked());
    await user.click(screen.getByRole("switch"));

    await waitFor(() => expect(setVisibility).toHaveBeenCalledWith(SUBJECT, true));
    expect(saveMyProfile).not.toHaveBeenCalled();
  });

  it("does not offer a release before there is a profile to release", async () => {
    // Eine Einwilligung, die auf nichts zeigt, wäre eine Zusage ins Leere —
    // und beim Widerruf müsste die Person erklären, was sie nie gezeigt hat.
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await screen.findByLabelText(/Überschrift/i);
    expect(screen.getByRole("switch")).toBeDisabled();
  });

  it("puts the switch back when the ledger refuses the change", async () => {
    const user = userEvent.setup();
    getMyProfile.mockResolvedValue(profile());
    setVisibility.mockResolvedValue({ ok: false, message: "Keine Verbindung zum Consent-Ledger." });
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await waitFor(() => expect(screen.getByRole("switch")).not.toBeChecked());
    await user.click(screen.getByRole("switch"));

    expect(await screen.findByRole("alert")).toHaveTextContent("Keine Verbindung");
    // Der Schalter darf nicht „sichtbar" zeigen, wenn nichts freigegeben wurde.
    expect(screen.getByRole("switch")).not.toBeChecked();
  });

  it("tells an anonymous visitor to log in instead of rendering an empty form", () => {
    renderWithProviders(<ProfileRoute principal={null} />);

    expect(screen.queryByLabelText(/Überschrift/i)).toBeNull();
    expect(screen.getByText(/anmelden/i)).toBeInTheDocument();
  });
});

describe("ProfileRoute — Formulierungshilfe", () => {
  it("asks for nothing until the person presses the button", async () => {
    // Kein Vorschlag von selbst. Der Text geht an einen fremden Anbieter, und
    // das passiert nur, wenn jemand es auslöst.
    getMyProfile.mockResolvedValue(profile());

    renderWithProviders(<ProfileRoute principal={principal()} />);

    await screen.findByDisplayValue("Senior Python");
    expect(draftProfileText).not.toHaveBeenCalled();
  });

  it("says at the button what leaves the platform — and what does not", async () => {
    // Nicht in einer Datenschutzerklärung: wer drückt, soll es gelesen haben.
    renderWithProviders(<ProfileRoute principal={principal()} />);

    const hint = await screen.findByText(/gehen dafür an Anthropic/i);
    expect(hint).toBeInTheDocument();
    expect(hint.textContent).toMatch(/Name und Adresse nicht/i);
    expect(hint.textContent).toMatch(/Gespeichert wird nichts/i);
  });

  it("puts the draft into the field and saves nothing on its own", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Vorschlag holen/i }));

    expect(await screen.findByDisplayValue("Ich baue Backends.")).toBeInTheDocument();
    // Der Entwurf ist ein Vorschlag, kein Ergebnis: gespeichert wird erst,
    // wenn die Person auf „Speichern" drückt.
    expect(saveMyProfile).not.toHaveBeenCalled();
  });

  it("warns before it overwrites work that is already there", async () => {
    // Ein Knopf, der ungefragt vorhandene Arbeit überschreibt, wird einmal
    // gedrückt und danach nie wieder.
    getMyProfile.mockResolvedValue(profile({ bio: "Mein eigener Text." }));

    renderWithProviders(<ProfileRoute principal={principal()} />);

    expect(
      await screen.findByRole("button", { name: /ersetzt den Text oben/i })
    ).toBeInTheDocument();
  });

  it("keeps the text when the provider is silent", async () => {
    const user = userEvent.setup();
    getMyProfile.mockResolvedValue(profile({ bio: "Mein eigener Text." }));
    draftProfileText.mockResolvedValue({
      ok: false,
      reason: "unavailable",
      message: "Die Formulierungshilfe ist gerade nicht verfügbar. Dein Text bleibt unverändert.",
    });

    renderWithProviders(<ProfileRoute principal={principal()} />);
    await user.click(await screen.findByRole("button", { name: /Vorschlag holen/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/nicht verfügbar/i);
    // Und der Text steht noch da.
    expect(screen.getByDisplayValue("Mein eigener Text.")).toBeInTheDocument();
  });

  it("does not submit the profile form", async () => {
    // `type="button"` fehlt schnell, und dann speichert der Hilfsknopf das
    // Profil, statt zu helfen.
    const user = userEvent.setup();
    renderWithProviders(<ProfileRoute principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Vorschlag holen/i }));

    expect(saveMyProfile).not.toHaveBeenCalled();
  });
});
