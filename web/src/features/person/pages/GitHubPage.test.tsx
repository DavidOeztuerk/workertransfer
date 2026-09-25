import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SIGNED_IN, PERSON, renderWithStore } from "../test/render";
import { stubNetwork } from "../test/network";
import { GitHubPage } from "./GitHubPage";

beforeEach(() => {
  stubNetwork({
    "GET /github/me": {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-07T00:00:00Z",
        repositories: [],
        languages_complete: true,
      },
    },
    "GET /github/oauth": { body: { available: false } },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("GitHubPage", () => {
  it("adopts skills only through the checkbox, not by itself", async () => {
    renderWithStore(<GitHubPage />, { auth: SIGNED_IN });

    expect(
      await screen.findByLabelText(/Als Fähigkeiten ins Profil übernehmen/i),
    ).not.toBeChecked();
  });
});
