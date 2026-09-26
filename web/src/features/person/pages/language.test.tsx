import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { i18n } from "../../../core/i18n/i18n";
import { SIGNED_IN, renderWithStore } from "../test/render";
import { MyDataPage } from "./MyDataPage";
import { ResumePage } from "./ResumePage";

/**
 * Texts that were written into the page instead of the catalogue, and so
 * stayed German (or German-formatted) in every language. Each case switches
 * the language — the one place outside language-journey that does — and puts
 * it back.
 */

function answer(route: (url: string) => { status?: number; body?: unknown }) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: string | URL | Request) => {
      const { status = 200, body = null } = route(String(input));
      return new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": "application/json" },
      });
    }),
  );
}

afterEach(async () => {
  cleanup();
  vi.unstubAllGlobals();
  await i18n.changeLanguage("de");
});

describe("in another language", () => {
  /*
   * The export FILE keeps its German section names — a data format, pinned by
   * consents-journey on the download. The PAGE drew those keys raw until
   * 26.09.2026 ("freigaben verlauf" for an English person).
   */
  it("my data names its sections in the chosen language", async () => {
    await i18n.changeLanguage("en");
    answer(() => ({ status: 500, body: { title: "Request failed", detail: "internal error" } }));
    renderWithStore(<MyDataPage />, { auth: SIGNED_IN });

    expect(await screen.findByText("History of releases")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/freigaben|lebenslauf|marktstatus/i);
  });

  it("the résumé writes the unit and the date the French way", async () => {
    await i18n.changeLanguage("fr");
    const documents = [
      DOCUMENT("lebenslauf", "Mon CV"),
      DOCUMENT("zeugnis", "Certificat de travail"),
    ];
    answer((url) => {
      if (url.includes("/resumes/me/documents")) return { body: documents };
      return { status: 404, body: { title: "Request failed", detail: "no resume" } };
    });
    renderWithStore(<ResumePage />, { auth: SIGNED_IN });

    expect((await screen.findAllByText(/20 Ko/)).length).toBeGreaterThan(0);
    expect(document.body.textContent).not.toMatch(/\d KB/);
    expect(document.body.textContent).toContain(
      new Date(UPLOADED).toLocaleDateString("fr"),
    );
  });
});

const UPLOADED = "2026-09-03T10:00:00Z";

const DOCUMENT = (kind: string, name: string) => ({
  id: `${kind}-1`,
  name,
  kind,
  content_type: "application/pdf",
  size_bytes: 20480,
  uploaded_at: UPLOADED,
});
