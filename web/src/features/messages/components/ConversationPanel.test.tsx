import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material/styles";
import { I18nextProvider } from "react-i18next";
import { afterEach, describe, expect, it, vi } from "vitest";

import { i18n } from "../../../core/i18n/i18n";
import { buildTheme } from "../../../styles/theme";
import { ConversationPanel } from "./ConversationPanel";

const APPLICATION = "aaaaaaaa-0000-4000-8000-000000000001";

type Answer = { status?: number; body?: unknown };
type Call = { url: string; method: string; body: unknown };

function stubFetch(routes: (url: string, method: string) => Answer): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const method = init?.method ?? "GET";
      calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : null });
      const answer = routes(url, method);
      return new Response(answer.status === 204 ? null : JSON.stringify(answer.body ?? null), {
        status: answer.status ?? 200,
        headers: { "content-type": "application/json" },
      });
    }),
  );
  return calls;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function draw() {
  return render(
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={buildTheme("light")}>
        <ConversationPanel application={APPLICATION} />
      </ThemeProvider>
    </I18nextProvider>,
  );
}

const SENT = {
  id: "m1",
  side: "company",
  text: "Wann passt Ihnen ein Gespräch?",
  state: "sent",
  own: false,
  created_at: "2026-10-07T08:00:00Z",
  sent_at: "2026-10-07T08:00:00Z",
};

const DRAFT = {
  id: "d1",
  side: "person",
  text: "Dienstag passt mir gut.",
  state: "draft",
  own: true,
  created_at: "2026-10-07T09:00:00Z",
  sent_at: null,
  origin: "agent",
};

describe("ConversationPanel", () => {
  it("always shows the hint about health data above the field", async () => {
    stubFetch(() => ({ body: { kind: "application", reference: APPLICATION, side: "person", messages: [] } }));
    draw();

    expect(await screen.findByText(/keine Gesundheitsdaten/i)).toBeInTheDocument();
    expect(screen.getByText(/Noch keine Nachricht/i)).toBeInTheDocument();
  });

  it("shows a draft as a draft, editable, and sends nothing on its own", async () => {
    const calls = stubFetch(() => ({
      body: { kind: "application", reference: APPLICATION, side: "person", messages: [SENT, DRAFT] },
    }));
    draw();

    const group = await screen.findByRole("group", { name: "Entwurf" });
    expect(group).toHaveTextContent(/Noch nicht gesendet/);
    expect(screen.getByLabelText("Entwurf bearbeiten")).toHaveValue("Dienstag passt mir gut.");
    expect(calls.filter((call) => call.method !== "GET")).toHaveLength(0);
  });

  it("releases the draft in the words standing in the field", async () => {
    const calls = stubFetch((url, method) =>
      method === "POST" && url.endsWith("/release")
        ? { body: { ...DRAFT, state: "sent", text: "Dienstag 10 Uhr passt." } }
        : { body: { kind: "application", reference: APPLICATION, side: "person", messages: [SENT, DRAFT] } },
    );
    draw();

    const field = await screen.findByLabelText("Entwurf bearbeiten");
    await userEvent.clear(field);
    await userEvent.type(field, "Dienstag 10 Uhr passt.");
    await userEvent.click(screen.getByRole("button", { name: "Freigeben und senden" }));

    await waitFor(() => expect(calls.some((call) => call.url.endsWith("/messages/drafts/d1/release"))).toBe(true));
    expect(calls.find((call) => call.url.endsWith("/release"))?.body).toEqual({ text: "Dienstag 10 Uhr passt." });
  });

  it("names a missing provider instead of a generic failure", async () => {
    stubFetch((url, method) =>
      method === "POST" && url.endsWith("/draft")
        ? { status: 503, body: { title: "Request failed", detail: "No draft provider is set up." } }
        : { body: { kind: "application", reference: APPLICATION, side: "person", messages: [] } },
    );
    draw();

    await userEvent.click(await screen.findByRole("button", { name: "Antwort entwerfen" }));

    expect(await screen.findByText(/kein Sprachmodell eingerichtet/i)).toBeInTheDocument();
  });

  it("does not show the other side's draft even if one arrived", async () => {
    stubFetch(() => ({
      body: {
        kind: "application",
        reference: APPLICATION,
        side: "person",
        messages: [{ ...DRAFT, side: "company", own: false, origin: undefined }],
      },
    }));
    draw();

    await screen.findByText(/keine Gesundheitsdaten/i);
    expect(screen.queryByRole("group", { name: "Entwurf" })).not.toBeInTheDocument();
  });
});
