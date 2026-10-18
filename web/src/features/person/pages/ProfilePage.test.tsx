import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { SIGNED_IN, PERSON, renderWithStore } from "../test/render";
import { type Network, stubNetwork } from "../test/network";
import { ProfilePage } from "./ProfilePage";

function repo(name: string, topics: string[], languages: string[] = []) {
  return {
    name,
    description: "",
    language: languages[0] ?? null,
    stars: 0,
    url: `https://github.com/anna/${name}`,
    pushed_at: null,
    languages,
    topics,
  };
}

const PROFILE = {
  subject_id: PERSON.userId,
  headline: "Backend development",
  bio: "",
  location: "Hamburg",
  remote_ok: false,
  skills: ["C#"],
  updated_at: "2026-09-01T10:00:00Z",
};

let wire: Network;

beforeEach(() => {
  wire = stubNetwork({
    "GET /profiles/me": { body: PROFILE },
    "PUT /profiles/me": { body: PROFILE },
    "POST /consent/check": { body: { granted: false } },
    "GET /github/me": { status: 404 },
    "GET /resumes/me": { status: 404 },
    "GET /resumes/me/documents/terms": { body: [] },
    "GET /portfolios/me": { status: 404 },
    "GET /account/address": {
      body: {
        line1: "",
        line2: "",
        postal_code: "",
        city: "",
        country: "DE",
        phone: "",
      },
    },
    "PUT /account/name": { body: { status: "ok" } },
    "PUT /account/address": {
      body: {
        line1: "",
        line2: "",
        postal_code: "",
        city: "",
        country: "DE",
        phone: "",
      },
    },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/**
 * <strong>Evidence becomes suggestions — never namings.</strong>
 *
 * This suite pins the seam ADR-0022 hangs on. What occurs in a repository is a
 * fact about a repository; that a person can do something is a statement about
 * a human, and only they may make it. Between the two lie two acts here: a
 * click fills the field, and only "Save" makes it a naming.
 */
describe("ProfilePage — suggestions from evidence", () => {
  it("adopts nothing into the profile by itself", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("service", ["kubernetes"], ["Go"])],
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    // The suggestions stand there …
    expect(await screen.findByRole("button", { name: /kubernetes/ })).toBeTruthy();

    // … and the field still carries only what the person said themselves.
    await waitFor(() =>
      expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#"),
    );
    expect(wire.last("PUT /profiles/me")).toBeUndefined();
  });

  it("puts a suggestion into the field — and does not save while doing so", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("service", ["kubernetes"], [])],
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /kubernetes/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, kubernetes");
    // The click is the one act, saving the other.
    expect(wire.last("PUT /profiles/me")).toBeUndefined();
  });

  it("makes it a naming only on save", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("service", ["kubernetes"], [])],
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /kubernetes/ }));
    await user.click(screen.getByRole("button", { name: /Speichern/ }));

    await waitFor(() =>
      expect(wire.last("PUT /profiles/me")).toBeDefined(),
    );
    expect(wire.last("PUT /profiles/me")?.body).toMatchObject({
      skills: ["C#", "kubernetes"],
    });
  });

  /**
   * <strong>The second source: the CV.</strong>
   *
   * Technologies at a position are words a human wrote themselves — just like
   * a GitHub topic. They therefore stand on an equal footing in the same
   * suggestion list, and they too become a statement about them only through a
   * click and a save.
   *
   * They do NOT make the position searchable: a CV is released individually
   * (ADR-0020). A skill becomes searchable only in the profile.
   */
  it("also suggests the technologies from the CV", async () => {
    wire.set("GET /resumes/me", {
      body: {
        subject_id: PERSON.userId,
        positions: [
          {
            employer: "Hansewerk",
            title: "Backend",
            started_on: "2022-04",
            ended_on: null,
            description: "",
            technologies: ["Kubernetes", "Terraform"],
          },
        ],
        education: [],
        updated_at: "2026-09-01T10:00:00Z",
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Kubernetes/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, Kubernetes");
  });

  /**
   * <strong>The third source: one's own portfolio.</strong>
   *
   * "I built this with that" is the same sentence about one's own work as at a
   * CV position — and therefore stands on an equal footing before the words
   * that merely stick to a repository.
   */
  it("also suggests the technologies of one's own portfolio items", async () => {
    wire.set("GET /portfolios/me", {
      body: {
        subject_id: PERSON.userId,
        items: [
          {
            title: "Load test",
            summary: "",
            url: null,
            role: "",
            year: null,
            attachment: null,
            technologies: ["Grafana"],
          },
        ],
        updated_at: "2026-09-01T10:00:00Z",
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Grafana/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, Grafana");
  });

  /**
   * Whoever has not linked GitHub must not look like an empty entry.
   *
   * ADR-0022 §3 says so explicitly: "Whoever has nothing on GitHub is not
   * worse, but elsewhere." A greyed-out box would be the silent claim that
   * something is missing there.
   */
  it("shows no suggestion area at all without a connected account", async () => {
    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    await screen.findByLabelText(/Fähigkeiten/);

    expect(screen.queryByText(/Aus deinen GitHub-Projekten/)).toBeNull();
  });

  it("suggests nothing that already stands in the profile", async () => {
    wire.set("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("service", ["c#"], [])],
      },
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    await screen.findByLabelText(/Fähigkeiten/);

    expect(screen.queryByText(/Aus deinen GitHub-Projekten/)).toBeNull();
  });
});

/**
 * <strong>The fourth source: one's own uploaded documents</strong>
 * (PBI-7, ADR-0043).
 *
 * It hangs on the same seam as the other three and makes no exception of it:
 * a reference becomes a suggestion, a suggestion becomes a naming only through
 * two acts. What is added here is the trigger — a reference is read only when
 * somebody asks for it.
 */
describe("ProfilePage — suggestions from one's own documents", () => {
  const ONE_REFERENCE = {
    document_id: "d1",
    name: "Job reference North",
    read_at: null,
    has_text: false,
    terms: [] as string[],
  };

  const READ = {
    ...ONE_REFERENCE,
    read_at: "2026-09-12T10:00:00Z",
    has_text: true,
    // Canonical vocabulary names — what resume-service's term finder returns.
    terms: ["Schweißfachmann", "CNC"],
  };

  /**
   * <strong>Without a trigger no document is read</strong> (ADR-0004).
   *
   * Opening a page must not open a reference. "On trigger" would otherwise be
   * a phrasing instead of a rule — a read when the page is called would
   * formally be triggered too, namely by the call.
   */
  it("reads no document when the page loads", async () => {
    wire.set("GET /resumes/me/documents/terms", { body: [ONE_REFERENCE] });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    await screen.findByRole("button", { name: /Meine Unterlagen lesen/ });

    expect(wire.last("POST /resumes/me/documents/read")).toBeUndefined();
  });

  /**
   * <strong>One click, and the reference says "Schweißfachmann".</strong>
   *
   * The acceptance from PBI-7: an uploaded PDF leads after ONE click to a
   * suggestion in the profile — and to no naming.
   */
  it("reads on click and suggests the words found", async () => {
    wire.set("GET /resumes/me/documents/terms", { body: [ONE_REFERENCE] });
    wire.set("POST /resumes/me/documents/read", { body: [READ] });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: /Meine Unterlagen lesen/ }),
    );

    expect(
      await screen.findByRole("button", { name: /Schweißfachmann/ }),
    ).toBeTruthy();

    // A suggestion, no naming: the field still carries only what the person
    // said themselves.
    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#");
    expect(wire.last("PUT /profiles/me")).toBeUndefined();
  });

  /**
   * <strong>None of it is searchable before the person has saved.</strong>
   *
   * This platform's search index is the PROFILE — the scout finds people by
   * the words they named themselves, never by a recognised text (ADR-0033). A
   * word gets in exclusively over <c>PUT /profiles/me</c>, and this test pins
   * the whole way: not after reading, not after the click, only after saving.
   *
   * <strong>The counter-probe fell.</strong> An <c>adopt</c> that saves on the
   * side turns the middle expectation red — and that would be exactly "a
   * recognised word lands in the search index without saving".
   */
  it("makes a recognised word a naming only on save", async () => {
    wire.set("GET /resumes/me/documents/terms", { body: [READ] });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    // Already read — and still nothing stands in the index.
    await screen.findByRole("button", { name: /Schweißfachmann/ });
    expect(wire.last("PUT /profiles/me")).toBeUndefined();

    await user.click(screen.getByRole("button", { name: /Schweißfachmann/ }));

    // The click fills the field — and sends nothing.
    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, Schweißfachmann");
    expect(wire.last("PUT /profiles/me")).toBeUndefined();

    await user.click(screen.getByRole("button", { name: /Speichern/ }));

    await waitFor(() => expect(wire.last("PUT /profiles/me")).toBeDefined());
    expect(wire.last("PUT /profiles/me")?.body).toMatchObject({
      skills: ["C#", "Schweißfachmann"],
    });
  });

  /**
   * <strong>"No text to read" is said, not kept quiet.</strong>
   *
   * A photographed master craftsman's certificate is a picture. Showing that as
   * "no suggestions" would be the silent claim that nothing stands in it —
   * ADR-0022 §3, a lie by omission.
   */
  it("says from which file no text could be read", async () => {
    wire.set("GET /resumes/me/documents/terms", {
      body: [
        {
          document_id: "d2",
          name: "Master certificate",
          read_at: "2026-09-12T10:00:00Z",
          has_text: false,
          terms: [],
        },
      ],
    });

    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    expect(await screen.findByText(/Master certificate/)).toBeTruthy();
    expect(screen.getByText(/die Datei ist ein Bild/)).toBeTruthy();
  });

  /**
   * Whoever uploaded nothing does not see the section at all.
   *
   * No empty box, no greyed-out button — the same rule as for a missing GitHub
   * account. That is the promise "nothing gets worse": an account without
   * documents sees exactly the same page as before.
   */
  it("shows no button without documents", async () => {
    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    await screen.findByLabelText(/Fähigkeiten/);

    expect(screen.queryByRole("button", { name: /Meine Unterlagen lesen/ })).toBeNull();
    expect(screen.queryByText(/Aus deinen Unterlagen/)).toBeNull();
  });
});

/**
 * The XING link (VISION-FRAGEN-2 N2) names the person — the form says so
 * before saving, the link goes out trimmed, an empty field takes it back.
 */
describe("ProfilePage — the XING link", () => {
  it("says before saving that the link shows the name", async () => {
    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });

    expect(await screen.findByLabelText(/XING-Profil/)).toBeInTheDocument();
    expect(screen.getByText(/Dein XING-Profil zeigt deinen Namen/)).toBeInTheDocument();
  });

  it("sends the link, and an emptied field takes it back", async () => {
    wire.set("GET /profiles/me", { body: { ...PROFILE, xing_url: "https://www.xing.com/profile/Anna_B" } });
    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    const field = await screen.findByLabelText(/XING-Profil/);
    await waitFor(() => expect(field).toHaveValue("https://www.xing.com/profile/Anna_B"));
    await user.click(screen.getByRole("button", { name: /Speichern/ }));
    await waitFor(() => expect(wire.last("PUT /profiles/me")).toBeDefined());
    expect(wire.last("PUT /profiles/me")?.body).toMatchObject({ xing_url: "https://www.xing.com/profile/Anna_B" });

    await user.clear(field);
    await user.click(screen.getByRole("button", { name: /Speichern/ }));
    await waitFor(() => expect(wire.last("PUT /profiles/me")?.body).toMatchObject({ xing_url: null }));
  });

  it("reads a refused address in German", async () => {
    wire.set("PUT /profiles/me", {
      status: 422,
      body: {
        title: "Request failed",
        detail: "The XING link must be a XING profile address (https://www.xing.com/profile/…).",
        correlationId: "c",
      },
    });
    renderWithStore(<ProfilePage />, { auth: SIGNED_IN });
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/XING-Profil/), "https://evil.example/x");
    await user.click(screen.getByRole("button", { name: /Speichern/ }));

    expect(await screen.findByText(/Bitte die Adresse deines XING-Profils angeben/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/must be a XING profile address/);
  });
});
