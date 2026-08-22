import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { MeResponse } from "../auth/client";
import type { Job } from "../jobs/client";
import { renderWithProviders } from "../test/render";
import { JobsRoute } from "./jobs";

vi.mock("../companies/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../companies/client")>();
  return { ...actual, getCompanyProfile: vi.fn() };
});
vi.mock("../jobs/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../jobs/client")>();
  return { ...actual, searchJobs: vi.fn() };
});
vi.mock("../profile/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../profile/client")>();
  return { ...actual, getMyProfile: vi.fn() };
});

const client = await import("../jobs/client");
const searchJobs = vi.mocked(client.searchJobs);
const profileClient = await import("../profile/client");
const getMyProfile = vi.mocked(profileClient.getMyProfile);

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
const companiesClient = await import("../companies/client");
const getCompanyProfile = vi.mocked(companiesClient.getCompanyProfile);

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: crypto.randomUUID(),
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

function principal(): MeResponse {
  return { user_id: "u", email: "anna@example.com", tenant_id: null };
}

beforeEach(() => {
  vi.clearAllMocks();
  searchJobs.mockResolvedValue({ ok: true, items: [], nextCursor: null });
  getCompanyProfile.mockResolvedValue(null);
  getMyProfile.mockResolvedValue(null);
});

// jsdom lässt `window.location` nicht beschreiben und kann keine echte
// Navigation ausführen. Ohne diesen Ersatz schreibt es
// „Not implemented: navigation to another Document" auf die Konsole und der
// Test kann über das Ziel nichts behaupten. Dieselbe Hilfe steht in
// login.test.tsx, dort mit der langen Begründung.
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

describe("JobsRoute — bewerben", () => {
  it("bietet Anonymen den Knopf an, aber kein Formular", async () => {
    // Die Aussage hat sich geändert, die Absicht nicht: bewerben kann nur, wer
    // angemeldet ist. Vorher stand hier „Zum Bewerben anmelden." mit einem Link
    // auf dem letzten Wort — für ein Programm anklickbar, für einen Menschen
    // ein Fließtext. Wer bewerben will, sucht einen Knopf.
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.getByRole("button", { name: /^Bewerben$/ })).toBeInTheDocument();
    // Aber eben kein Bewerbungsformular: der Knopf führt zur Anmeldung.
    expect(screen.queryByLabelText(/Anschreiben/i)).toBeNull();
    expect(screen.getByText(/danach geht es direkt zur Bewerbung/i)).toBeInTheDocument();
  });

  it("führt Angemeldete auf die Bewerbungsseite dieser Stelle, nicht in ein Formular in der Karte", async () => {
    // Das Formular hat eine eigene Adresse (`/jobs/<id>/apply`). Vorher klappte
    // es in der Karte auf: nicht teilbar, nicht neu ladbar, und die gemerkte
    // Absicht nach dem Anmelden musste auf eine gefilterte Liste zielen, in der
    // die Stelle womöglich gar nicht vorkam.
    const stelle = job();
    searchJobs.mockResolvedValue({ ok: true, items: [stelle], nextCursor: null });

    renderWithProviders(<JobsRoute principal={principal()} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.getByRole("link", { name: /^Bewerben$/ })).toHaveAttribute(
      "href",
      `/jobs/${stelle.id}/apply`
    );
    expect(screen.queryByLabelText(/Anschreiben/i)).toBeNull();
  });

  it("merkt sich die Stelle UND wechselt zur Anmeldung", async () => {
    // Ohne das müsste man nach dem Anmelden die Suche wiederholen — nur weil
    // man kein Konto hatte.
    //
    // Geprüft werden BEIDE Hälften. Vorher behauptete der Test nur das Merken;
    // das Wechseln lief gegen jsdom, das echte Navigation nicht kann und sie
    // mit „Not implemented: navigation to another Document" auf die Konsole
    // schrieb — eine Meldung im grünen Lauf, und niemand hätte gemerkt, wenn
    // das Ziel verstellt worden wäre.
    const user = userEvent.setup();
    const location = stubLocation();
    // EINMAL festhalten: `job()` würfelt bei jedem Aufruf eine neue ID.
    const stelle = job();
    searchJobs.mockResolvedValue({ ok: true, items: [stelle], nextCursor: null });
    window.localStorage.clear();

    renderWithProviders(<JobsRoute principal={null} />);
    await screen.findByText("Backend-Entwicklerin");
    await user.click(screen.getByRole("button", { name: /^Bewerben$/ }));

    const gemerkt = JSON.parse(window.localStorage.getItem("wt.gemerkte-stelle") ?? "null");
    expect(gemerkt?.jobId).toBe(stelle.id);
    // Der Titel reist mit, damit die Anmeldeseite ihn nennen kann, ohne
    // dafür eine Abfrage zu brauchen.
    expect(gemerkt?.titel).toBe("Backend-Entwicklerin");
    // Erst merken, dann wechseln — genau in dieser Reihenfolge, sonst ist die
    // Absicht beim Ankommen noch nicht da.
    expect(location.href).toBe("/login");
  });

  // Das Bewerbungsformular selbst steht in `job-apply.test.tsx` — mitsamt den
  // Zusagen, die daran hängen: kein Kästchen für das Profil, es wird geschickt
  // was angehakt war, der Weg zum Zurückziehen, und ein stummer Dienst ist
  // keine Ablehnung.
});

describe("JobsRoute — Passung", () => {
  const WITH_SKILLS = { skills: ["Python", "Kubernetes", "Go"] };

  it("names what you have and what you lack — and no percentage anywhere", async () => {
    // Eine Prozentzahl sieht aus wie eine Messung und ist eine Division. Sie
    // verschweigt genau das, was zählt: WELCHE Fähigkeit fehlt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["python", "Kubernetes"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/2 von 3/)).toBeInTheDocument();
    expect(screen.getByText(/^Go$/)).toBeInTheDocument();
    expect(screen.queryByText(/%/)).toBeNull();
  });

  it("marks the missing one as missing, not merely as absent from the list", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    // Der fehlende Teil ist der, der etwas nützt: er sagt, was man tun könnte.
    const missing = await screen.findByText(/^Go$/);
    expect(missing).toHaveAttribute("data-match", "missing");
    expect(screen.getByText(/^Python$/)).toHaveAttribute("data-match", "have");
  });

  it("asks nothing about a person who is not logged in", async () => {
    // Ohne Anmeldung gibt es kein Profil, also nichts zu vergleichen — die
    // Fähigkeiten stehen trotzdem da, sie gehören zur Ausschreibung.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });

    renderWithProviders(<JobsRoute principal={null} />);

    expect(await screen.findByText(/^Go$/)).toBeInTheDocument();
    expect(screen.queryByText(/von 3/)).toBeNull();
    expect(getMyProfile).not.toHaveBeenCalled();
  });

  it("says 'nichts eingetragen' instead of '0 von 3'", async () => {
    // „0 von 3" wäre eine Aussage über den Menschen, die nicht stimmt: er hat
    // nichts gesagt, nicht nichts gekonnt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile([]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/Profil/i)).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).toBeNull();
  });

  it("points at the profile of someone who has none at all, not just an empty one", async () => {
    // `getMyProfile()` liefert `null` für „noch keins angelegt". Ohne diesen
    // Fall bliebe die Seite genau dort stumm, wo ein Satz die ganze Funktion
    // erklärt.
    searchJobs.mockResolvedValue({ ok: true, items: [job(WITH_SKILLS)], nextCursor: null });
    getMyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={principal()} />);

    expect(await screen.findByText(/Trage Fähigkeiten in deinem/i)).toBeInTheDocument();
    expect(screen.queryByText(/von 3/)).toBeNull();
  });

  it("shows no skill line at all when the job names none", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ skills: [] })], nextCursor: null });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByText(/von 0/)).toBeNull();
    expect(screen.queryByText(/genannten Fähigkeiten/)).toBeNull();
  });

  it("asks for the profile once, not once per job", async () => {
    searchJobs.mockResolvedValue({
      ok: true,
      items: [job({ id: "a", ...WITH_SKILLS }), job({ id: "b", ...WITH_SKILLS })],
      nextCursor: null,
    });
    getMyProfile.mockResolvedValue(myProfile(["Python"]));

    renderWithProviders(<JobsRoute principal={principal()} />);

    await screen.findAllByText("Backend-Entwicklerin");
    await waitFor(() => expect(getMyProfile).toHaveBeenCalled());
    expect(getMyProfile).toHaveBeenCalledTimes(1);
  });
});

describe("JobsRoute — wer sucht", () => {
  it("names the company on the card", async () => {
    searchJobs.mockResolvedValue({ ok: true, items: [job({ tenant_id: "t1" })], nextCursor: null });
    getCompanyProfile.mockResolvedValue({
      tenant_id: "t1",
      slug: "muster",
      display_name: "Muster",
      about: "",
      website: "https://muster.example",
      locations: [],
      benefits: ["Homeoffice"],
      updated_at: "2026-08-02T10:00:00Z",
    });

    renderWithProviders(<JobsRoute principal={null} />);

    expect(await screen.findByText("Muster")).toBeInTheDocument();
    expect(screen.getByText(/Homeoffice/)).toBeInTheDocument();
  });

  it("shows nothing rather than a placeholder when there is no profile", async () => {
    // „Unbekanntes Unternehmen" wäre eine Aussage, die niemand gemacht hat.
    searchJobs.mockResolvedValue({ ok: true, items: [job()], nextCursor: null });
    getCompanyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findByText("Backend-Entwicklerin");
    expect(screen.queryByText(/unbekannt/i)).toBeNull();
  });

  it("asks once per company, not once per job", async () => {
    // Der Query-Key hängt am Unternehmen; mehrere Stellen desselben
    // Arbeitgebers teilen sich eine Abfrage.
    searchJobs.mockResolvedValue({
      ok: true,
      items: [job({ id: "a", tenant_id: "t1" }), job({ id: "b", tenant_id: "t1" })],
      nextCursor: null,
    });
    getCompanyProfile.mockResolvedValue(null);

    renderWithProviders(<JobsRoute principal={null} />);

    await screen.findAllByText("Backend-Entwicklerin");
    await waitFor(() => expect(getCompanyProfile).toHaveBeenCalled());
    expect(getCompanyProfile).toHaveBeenCalledTimes(1);
  });
});
