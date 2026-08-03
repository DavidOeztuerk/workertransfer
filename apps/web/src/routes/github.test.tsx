import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { GitHubConnection } from "../github/client";
import { renderWithProviders } from "../test/render";
import { GitHubRoute } from "./github";

vi.mock("../github/client", async (o) => ({
  ...(await o<typeof import("../github/client")>()),
  getMyGitHub: vi.fn(),
  connectGitHub: vi.fn(),
  verifyGitHub: vi.fn(),
  refreshGitHub: vi.fn(),
  disconnectGitHub: vi.fn(),
}));

const client = await import("../github/client");
const getMyGitHub = vi.mocked(client.getMyGitHub);
const connectGitHub = vi.mocked(client.connectGitHub);
const verifyGitHub = vi.mocked(client.verifyGitHub);
const disconnectGitHub = vi.mocked(client.disconnectGitHub);

const SUBJECT = "11111111-1111-1111-1111-111111111111";

function principal(): MeResponse {
  return { user_id: SUBJECT, email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

function connection(overrides: Partial<GitHubConnection> = {}): GitHubConnection {
  return {
    subject_id: SUBJECT,
    login: "anna",
    verified: false,
    challenge_description: "workertransfer-verify-abc123",
    fetched_at: null,
    repositories: [],
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  getMyGitHub.mockResolvedValue(null);
  connectGitHub.mockResolvedValue({ ok: true, connection: connection() });
  verifyGitHub.mockResolvedValue({ ok: true, connection: connection({ verified: true }) });
  disconnectGitHub.mockResolvedValue(true);
});

describe("GitHubRoute", () => {
  it("asks for a login rather than showing a connection nobody owns", async () => {
    renderWithProviders(<GitHubRoute principal={null} />);

    expect(await screen.findByRole("link", { name: "anmelden" })).toBeTruthy();
    expect(getMyGitHub).not.toHaveBeenCalled();
  });

  it("says plainly that it shows evidence, not a score", async () => {
    // Die Zusage gehört auf die Seite, auf der jemand sein Konto verbindet —
    // nicht in eine ADR, die er nie liest.
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByText(/Belege, keine Noten/)).toBeTruthy();
    expect(screen.getByText(/keine Punktzahl und keine/)).toBeTruthy();
  });

  it("says that nothing is fetched in the background", async () => {
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByText(/kein Abgleich im Hintergrund/)).toBeTruthy();
  });

  it("shows the exact line that has to go into the gist", async () => {
    getMyGitHub.mockResolvedValue(connection());
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByText("workertransfer-verify-abc123")).toBeTruthy();
  });

  it("sends the login when connecting", async () => {
    renderWithProviders(<GitHubRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/GitHub-Benutzername/), "anna");
    await user.click(screen.getByRole("button", { name: "Weiter" }));

    await waitFor(() => expect(connectGitHub).toHaveBeenCalledWith("anna"));
  });

  it("keeps a missing gist apart from an outage", async () => {
    // Ein Ausfall darf nicht wie ein fehlender Nachweis aussehen: das hieße,
    // jemandem den Nachweis abzusprechen, weil WIR nicht fragen konnten.
    getMyGitHub.mockResolvedValue(connection());
    verifyGitHub.mockResolvedValue({
      ok: false,
      reason: "not-proven",
      message: "Kein öffentlicher Gist mit dieser Beschreibung gefunden.",
    });
    renderWithProviders(<GitHubRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Nachweis prüfen" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/Gist/);
  });

  it("shows the snapshot with its date and never a score", async () => {
    getMyGitHub.mockResolvedValue(
      connection({
        verified: true,
        fetched_at: "2026-08-03T10:00:00Z",
        repositories: [
          {
            name: "etwas",
            description: "Ein Werkzeug",
            language: "Python",
            stars: 3,
            url: "https://github.com/anna/etwas",
            pushed_at: "2026-08-01T10:00:00Z",
          },
        ],
      })
    );
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByRole("link", { name: "etwas" })).toBeTruthy();
    expect(screen.getByText(/Stand:/)).toBeTruthy();
    expect(screen.queryByText(/Punkte|Score|Bewertung|\/100/)).toBeNull();
  });

  it("says an empty result is an answer, not a shortcoming", async () => {
    getMyGitHub.mockResolvedValue(connection({ verified: true, repositories: [] }));
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByText(/kein Mangel/)).toBeTruthy();
  });

  it("points at the ledger, because connecting is not showing", async () => {
    getMyGitHub.mockResolvedValue(connection({ verified: true }));
    renderWithProviders(<GitHubRoute principal={principal()} />);

    expect(await screen.findByRole("link", { name: "Meine Freigaben" })).toBeTruthy();
  });

  it("disconnects", async () => {
    getMyGitHub.mockResolvedValue(connection({ verified: true }));
    renderWithProviders(<GitHubRoute principal={principal()} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Verbindung trennen" }));

    await waitFor(() => expect(disconnectGitHub).toHaveBeenCalled());
  });
});
