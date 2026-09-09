import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ANGEMELDET, PERSON, renderMitStore } from "../test/render";
import { netz } from "../test/netz";
import { GitHubPage } from "./GitHubPage";

beforeEach(() => {
  netz({
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
  it("übernimmt Fähigkeiten nur über die Checkbox, nicht von allein", async () => {
    renderMitStore(<GitHubPage />, { auth: ANGEMELDET });

    expect(
      await screen.findByLabelText(/Als Fähigkeiten ins Profil übernehmen/i),
    ).not.toBeChecked();
  });
});
