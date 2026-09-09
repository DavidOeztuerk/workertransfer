import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ANGEMELDET, PERSON, renderMitStore } from "../test/render";
import { type Netz, netz } from "../test/netz";
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

const PROFIL = {
  subject_id: PERSON.userId,
  headline: "Backend-Entwicklung",
  bio: "",
  location: "Hamburg",
  remote_ok: false,
  skills: ["C#"],
  updated_at: "2026-09-01T10:00:00Z",
};

let draht: Netz;

beforeEach(() => {
  draht = netz({
    "GET /profiles/me": { body: PROFIL },
    "PUT /profiles/me": { body: PROFIL },
    "POST /consent/check": { body: { granted: false } },
    "GET /github/me": { status: 404 },
    "GET /resumes/me": { status: 404 },
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
 * <strong>Aus Belegen werden Vorschläge — nie Nennungen.</strong>
 *
 * Diese Reihe hält die Naht fest, an der ADR-0022 hängt. Was in einem
 * Repository vorkommt, ist eine Tatsache über ein Repository; dass eine Person
 * etwas kann, ist eine Aussage über einen Menschen, und die darf nur sie selbst
 * treffen. Zwischen beidem liegen hier zwei Handlungen: ein Klick füllt das
 * Feld, und erst „Speichern" macht daraus eine Nennung.
 */
describe("ProfilePage — Vorschläge aus Belegen", () => {
  it("übernimmt von allein nichts ins Profil", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("dienst", ["kubernetes"], ["Go"])],
      },
    });

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });

    // Die Vorschläge stehen da …
    expect(await screen.findByRole("button", { name: /kubernetes/ })).toBeTruthy();

    // … und das Feld trägt weiterhin nur, was die Person selbst gesagt hat.
    await waitFor(() =>
      expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#"),
    );
    expect(draht.letzter("PUT /profiles/me")).toBeUndefined();
  });

  it("setzt einen Vorschlag ins Feld — und speichert dabei nicht", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("dienst", ["kubernetes"], [])],
      },
    });

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /kubernetes/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, kubernetes");
    // Der Klick ist die eine Handlung, das Speichern die andere.
    expect(draht.letzter("PUT /profiles/me")).toBeUndefined();
  });

  it("macht erst mit dem Speichern eine Nennung daraus", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("dienst", ["kubernetes"], [])],
      },
    });

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /kubernetes/ }));
    await user.click(screen.getByRole("button", { name: /Speichern/ }));

    await waitFor(() =>
      expect(draht.letzter("PUT /profiles/me")).toBeDefined(),
    );
    expect(draht.letzter("PUT /profiles/me")?.body).toMatchObject({
      skills: ["C#", "kubernetes"],
    });
  });

  /**
   * <strong>Die zweite Quelle: der Lebenslauf.</strong>
   *
   * Technologien an einer Station sind Wörter, die ein Mensch selbst
   * hingeschrieben hat — genau wie ein GitHub-Topic. Sie stehen deshalb
   * gleichberechtigt in derselben Vorschlagsliste, und auch sie werden erst
   * durch einen Klick und ein Speichern zu einer Aussage über ihn.
   *
   * Sie machen die Station NICHT durchsuchbar: ein Lebenslauf ist einzeln
   * freigegeben (ADR-0020). Suchbar wird eine Fähigkeit nur im Profil.
   */
  it("schlägt auch die Technologien aus dem Lebenslauf vor", async () => {
    draht.setze("GET /resumes/me", {
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

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Kubernetes/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, Kubernetes");
  });

  /**
   * <strong>Die dritte Quelle: die eigenen Arbeiten.</strong>
   *
   * „Damit habe ich das gebaut" ist derselbe Satz über die eigene Arbeit wie an
   * einer Lebenslauf-Station — und steht deshalb gleichberechtigt vor den
   * Wörtern, die nur an einem Repository kleben.
   */
  it("schlägt auch die Technologien der eigenen Arbeiten vor", async () => {
    draht.setze("GET /portfolios/me", {
      body: {
        subject_id: PERSON.userId,
        items: [
          {
            title: "Lastprobe",
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

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Grafana/ }));

    expect(screen.getByLabelText(/Fähigkeiten/)).toHaveValue("C#, Grafana");
  });

  /**
   * Wer kein GitHub verknüpft hat, darf nicht wie ein leerer Eintrag aussehen.
   *
   * ADR-0022 §3 nennt das ausdrücklich: „Wer nichts auf GitHub hat, ist nicht
   * schlechter, sondern woanders." Ein ausgegrauter Kasten wäre die
   * stillschweigende Behauptung, dort fehle etwas.
   */
  it("zeigt ohne verbundenes Konto gar keinen Vorschlagsbereich", async () => {
    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });

    await screen.findByLabelText(/Fähigkeiten/);

    expect(screen.queryByText(/Aus deinen GitHub-Projekten/)).toBeNull();
  });

  it("schlägt nichts vor, was schon im Profil steht", async () => {
    draht.setze("GET /github/me", {
      body: {
        subject_id: PERSON.userId,
        login: "anna",
        verified: true,
        challenge_description: null,
        fetched_at: "2026-09-01T10:00:00Z",
        repositories: [repo("dienst", ["c#"], [])],
      },
    });

    renderMitStore(<ProfilePage />, { auth: ANGEMELDET });

    await screen.findByLabelText(/Fähigkeiten/);

    expect(screen.queryByText(/Aus deinen GitHub-Projekten/)).toBeNull();
  });
});
