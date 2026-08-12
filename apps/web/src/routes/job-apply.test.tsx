import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Job } from "../jobs/client";
import { renderWithProviders } from "../test/render";
import { JobApplyRoute } from "./job-apply";

vi.mock("../applications/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../applications/client")>();
  return { ...actual, apply: vi.fn() };
});
vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return { ...actual, getJob: vi.fn() };
});
vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return { ...actual, getMyProfile: vi.fn() };
});

const jobsClient = await import("../jobs/client");
const getJob = vi.mocked(jobsClient.getJob);
const applicationsClient = await import("../applications/client");
const apply = vi.mocked(applicationsClient.apply);
const profileClient = await import("../profile/client");
const getMyProfile = vi.mocked(profileClient.getMyProfile);

const STELLE = "11111111-2222-3333-4444-555555555555";

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: STELLE,
    tenant_id: "t",
    title: "Backend-Entwicklerin",
    description: "Was zu tun ist.",
    location: "Berlin",
    remote: "hybrid",
    employment: "full_time",
    skills: [],
    status: "published",
    published_at: "2026-08-02T10:00:00Z",
    updated_at: "2026-08-02T10:00:00Z",
    ...overrides,
  };
}

function myProfile(skills: string[]) {
  return {
    subject_id: "u",
    headline: "Entwicklerin",
    bio: "",
    location: "Berlin",
    remote_ok: true,
    skills,
    updated_at: "2026-08-02T10:00:00Z",
  };
}

function principal(): MeResponse {
  return { user_id: "u", email: "anna@example.com", tenant_id: null, roles: ["user"] };
}

beforeEach(() => {
  vi.clearAllMocks();
  getJob.mockResolvedValue(job());
  getMyProfile.mockResolvedValue(null);
  apply.mockResolvedValue({
    ok: true,
    application: {
      id: "a",
      job_id: STELLE,
      tenant_id: "t",
      subject_id: "u",
      message: "",
      shares_resume: true,
      shares_portfolio: false,
      status: "submitted",
      created_at: "2026-08-02T10:00:00Z",
      updated_at: "2026-08-02T10:00:00Z",
    },
  });
});

// Wie in jobs.test.tsx und login.test.tsx: jsdom kann keine echte Navigation und
// schreibt sonst „Not implemented: navigation to another Document" auf die
// Konsole — eine Meldung im grünen Lauf, und über das Ziel könnte der Test
// nichts behaupten.
function stubLocation(): { readonly href: string } {
  let href = "";
  const stub = {
    get href() {
      return href;
    },
    set href(value: string) {
      href = value;
    },
  };
  Object.defineProperty(window, "location", { value: stub, writable: true, configurable: true });
  return {
    get href() {
      return href;
    },
  };
}

describe("JobApplyRoute", () => {
  it("does not offer a checkbox for the profile — it is not a choice", async () => {
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(await screen.findByLabelText(/Lebenslauf/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Profil$/i)).toBeNull();
  });

  it("sends what was ticked", async () => {
    const user = userEvent.setup();
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    await user.click(await screen.findByLabelText(/Meine Arbeiten/i));
    await user.click(screen.getByRole("button", { name: /Bewerbung abschicken/i }));

    await waitFor(() => expect(apply).toHaveBeenCalled());
    expect(apply.mock.calls[0]?.[0]).toMatchObject({
      job_id: STELLE,
      shares_resume: true,
      shares_portfolio: true,
    });
  });

  it("says how to undo it, right where it was done", async () => {
    // Die Freigabe ist der Punkt; wo man sie zurücknimmt, gehört daneben.
    const user = userEvent.setup();
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Bewerbung abschicken/i }));

    expect(await screen.findByText(/Zurückziehen kannst du sie jederzeit/i)).toBeInTheDocument();
    // Und das Formular ist weg — sonst sähe es aus, als könne man dasselbe
    // gleich noch einmal abschicken.
    expect(screen.queryByLabelText(/Anschreiben/i)).toBeNull();
  });

  it("does not call a silent dependency a rejection", async () => {
    const user = userEvent.setup();
    apply.mockResolvedValue({
      ok: false,
      reason: "unavailable",
      message: "Ein beteiligter Dienst antwortet gerade nicht.",
    });
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    await user.click(await screen.findByRole("button", { name: /Bewerbung abschicken/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("antwortet gerade nicht");
    expect(alert.textContent).not.toMatch(/abgelehnt/i);
  });

  it("nennt die Stelle, auf die man sich bewirbt", async () => {
    // Eine eigene Adresse muss selbst sagen, worum es geht — vorher stand das
    // Formular in der Karte der Stelle und bezog den Zusammenhang von dort.
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Backend-Entwicklerin" })
    ).toBeInTheDocument();
  });

  it("bietet den Rückweg an — auf einem kalten Deep-Link ist die Seite sonst eine Sackgasse", async () => {
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    await screen.findByLabelText(/Anschreiben/i);
    expect(screen.getByRole("link", { name: /Zurück zu den offenen Stellen/i })).toHaveAttribute(
      "href",
      "/jobs"
    );
  });

  it("sagt beim Laden, dass geladen wird — statt ein leeres Formular zu zeigen", async () => {
    let loesen: ((job: Job) => void) | undefined;
    getJob.mockReturnValue(
      new Promise<Job | null>((resolve) => {
        loesen = resolve as (job: Job) => void;
      })
    );

    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(await screen.findByRole("status")).toHaveTextContent(/Stelle wird geladen/i);
    // Und vor allem: kein Formular, das man abschicken könnte, ohne dass die
    // Stelle überhaupt bekannt ist.
    expect(screen.queryByRole("button", { name: /Bewerbung abschicken/i })).toBeNull();
    loesen?.(job());
  });

  it("behandelt eine zurückgezogene Stelle wie eine, die es nie gab", async () => {
    // Welche von beiden es war, ist eine Aussage über das Unternehmen — und die
    // steht hier niemandem zu.
    getJob.mockResolvedValue(null);

    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(
      await screen.findByRole("heading", { level: 1, name: /Diese Stelle gibt es nicht/i })
    ).toBeInTheDocument();
    expect(screen.queryByLabelText(/Anschreiben/i)).toBeNull();
  });

  it("fragt bei einer unsinnigen ID gar nicht erst nach", async () => {
    // Die ID kommt aus der Adresszeile. Sie ungeprüft in einen Pfad zu setzen
    // wäre der eine Ort, an dem fremde Eingabe zur Anfrage wird.
    renderWithProviders(<JobApplyRoute jobId="../../etc/passwd" principal={principal()} />);

    await screen.findByRole("heading", { level: 1, name: /Diese Stelle gibt es nicht/i });
    expect(getJob).not.toHaveBeenCalled();
  });

  it("merkt sich die Absicht, wenn jemand ohne Konto hier landet", async () => {
    // Der geteilte Link ist der Normalfall dafür: jemand schickt eine Stelle
    // weiter, der Empfänger hat kein Konto. Ohne das Merken müsste er die
    // Stelle nach dem Anmelden erneut suchen.
    const user = userEvent.setup();
    const location = stubLocation();
    window.localStorage.clear();

    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={null} />);

    // Kein Formular für Nichtangemeldete — bewerben kann nur, wer ein Konto hat.
    expect(await screen.findByRole("button", { name: /Anmelden und bewerben/i })).toBeVisible();
    expect(screen.queryByLabelText(/Anschreiben/i)).toBeNull();

    await user.click(screen.getByRole("button", { name: /Anmelden und bewerben/i }));

    const gemerkt = JSON.parse(window.localStorage.getItem("wt.gemerkte-stelle") ?? "null");
    expect(gemerkt?.jobId).toBe(STELLE);
    expect(gemerkt?.titel).toBe("Backend-Entwicklerin");
    // Erst merken, dann wechseln — sonst ist die Absicht beim Ankommen nicht da.
    expect(location.href).toBe("/login");
  });

  it("fragt nichts über eine Person, die nicht angemeldet ist", async () => {
    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={null} />);

    await screen.findByRole("button", { name: /Anmelden und bewerben/i });
    expect(getMyProfile).not.toHaveBeenCalled();
  });

  it("zeigt die Passung beim Schreiben — als Liste, nicht als Zahl", async () => {
    // Beim Formulieren hilft genau das: zu sehen, welche Fähigkeit fehlt.
    getJob.mockResolvedValue(job({ skills: ["Python", "Kubernetes", "Go"] }));
    getMyProfile.mockResolvedValue(myProfile(["Python", "Kubernetes"]));

    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(await screen.findByText(/Du hast 2 von 3 genannten Fähigkeiten/i)).toBeInTheDocument();
    // Kein Prozentwert, nirgends (ADR-0022).
    expect(document.body.textContent).not.toMatch(/\d+\s?%/);
    expect(screen.getByText("Go").closest("li")).toHaveTextContent("fehlt dir");
  });

  it("sagt jemandem ohne eingetragene Fähigkeiten nicht „0 von 3“", async () => {
    getJob.mockResolvedValue(job({ skills: ["Python", "Kubernetes", "Go"] }));
    getMyProfile.mockResolvedValue(myProfile([]));

    renderWithProviders(<JobApplyRoute jobId={STELLE} principal={principal()} />);

    expect(await screen.findByText(/Trage Fähigkeiten in deinem/i)).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).toBeNull();
  });
});
