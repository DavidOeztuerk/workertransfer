import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { Job } from "../jobs/client";
import { renderWithProviders } from "../test/render";
import { JobsRoute } from "./jobs";

vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return { ...actual, searchJobs: vi.fn() };
});

const client = await import("../jobs/client");
const searchJobs = vi.mocked(client.searchJobs);

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: crypto.randomUUID(),
    tenant_id: "t",
    title: "Backend-Entwicklerin",
    description: "Was zu tun ist.",
    location: "Berlin",
    remote: "hybrid",
    employment: "full_time",
    status: "published",
    published_at: "2026-08-02T10:00:00Z",
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  searchJobs.mockResolvedValue({ ok: true, items: [], nextCursor: null });
});

describe("JobsRoute", () => {
  it("searches without a login — no principal needed at all", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText("Backend-Entwicklerin")).toBeInTheDocument();
  });

  it("says an empty result means nothing matched, not that something broke", async () => {
    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/keine.*gefunden|nichts gefunden/i)).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("passes the typed query to the server", async () => {
    const user = userEvent.setup();
    renderWithProviders(<JobsRoute />);

    await user.type(await screen.findByLabelText(/Suchbegriff/i), "python");
    await user.click(screen.getByRole("button", { name: /Suchen/i }));

    await waitFor(() =>
      expect(searchJobs).toHaveBeenLastCalledWith(expect.objectContaining({ q: "python" }))
    );
  });

  it("spells out what hybrid means instead of showing the raw value", async () => {
    // "hybrid" ist ein Wert im Vertrag, kein Satz für Menschen.
    searchJobs.mockResolvedValue({ ok: true, items: [job({ remote: "full" })], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/vollständig remote/i)).toBeInTheDocument();
    expect(screen.queryByText("full")).toBeNull();
  });

  it("says 'Ort nicht angegeben' rather than leaving a gap", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ location: "" })], nextCursor: null });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByText(/nicht angegeben/i)).toBeInTheDocument();
  });

  it("loads the next page from the cursor and keeps the first", async () => {
    const user = userEvent.setup();
    searchJobs.mockResolvedValueOnce({
      ok: true,
      items: [job({ title: "Erste" })],
      nextCursor: "cursor-1",
    });
    searchJobs.mockResolvedValueOnce({
      ok: true,
      items: [job({ title: "Zweite" })],
      nextCursor: null,
    });

    renderWithProviders(<JobsRoute />);

    await user.click(await screen.findByRole("button", { name: /Mehr laden/i }));

    await waitFor(() =>
      expect(searchJobs).toHaveBeenLastCalledWith(expect.objectContaining({ cursor: "cursor-1" }))
    );
    expect(await screen.findByText("Zweite")).toBeInTheDocument();
    expect(screen.getByText("Erste")).toBeInTheDocument();
  });

  it("reports a failed search instead of showing an empty list", async () => {
    // Eine leere Liste wäre die Behauptung, es gebe nichts.
    searchJobs.mockResolvedValue({ ok: false, message: "Die Suche ist fehlgeschlagen." });

    renderWithProviders(<JobsRoute />);

    expect(await screen.findByRole("alert")).toHaveTextContent("fehlgeschlagen");
    expect(screen.queryByText(/nichts gefunden/i)).toBeNull();
  });
});
