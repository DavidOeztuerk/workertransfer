import userEvent from "@testing-library/user-event";
import { cleanup, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { vergissKontext } from "../lib/kontext";
import { renderMitStore } from "../test/render";
import { JobsPage } from "./JobsPage";

/**
 * Gestubbt wird `fetch`, nicht das Client-Modul.
 *
 * Die alten Tests mockten `../jobs/client` — damit prüften sie die Seite gegen
 * eine Erfindung. Der Draht-Fund (`19f45b6`: camelCase kam beim Server nicht an)
 * versteckte sich genau in der Schicht, die dabei übersprungen wurde. Hier
 * laufen Pfad, Methode und snake_case durch den echten Client.
 */
type Antwort = { status?: number; body?: unknown };

function stubFetch(
  routen: (url: string, init: RequestInit | undefined) => Antwort,
) {
  const spion = vi.fn(
    async (input: string | URL | Request, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const answer = routen(url, init);
      const status = answer.status ?? 200;
      return new Response(
        status === 204 ? null : JSON.stringify(answer.body ?? null),
        {
          status,
          headers: { "content-type": "application/json" },
        },
      );
    },
  );
  vi.stubGlobal("fetch", spion);
  return spion;
}

/**
 * Eine Stelle, WIE DER DIENST SIE SCHICKT — nicht, wie die Oberfläche sie hält.
 *
 * <strong>Hier stand einmal `remote` und `employment`.</strong> Der Dienst
 * schreibt aber `remote_mode` und `employment_type`. Die ganze Reihe war grün,
 * während die Karte in der laufenden Anlage
 * `stelle.remoteundefined · stelle.employmentundefined` zeigte — weil der Test
 * eine Gestalt erfand, die es auf dem Draht nie gab, und `searchJobs` die
 * Antwort ungeprüft auf `Job` castete.
 *
 * Die Regel dahinter steht in CLAUDE.md beim Benachrichtigungsdraht: einen
 * Namen auf beiden Seiten selbst zu schreiben ist eine Zeichenkette, die man
 * zweimal prüft. Ein Fixture muss den DRAHT nachbilden.
 *
 * Und vollständig gefüllt, jedes Feld: ein Fixture mit leeren Feldern lässt
 * genau die Zweige ungeprüft, die etwas anzeigen.
 */
const STELLE = {
  id: "11111111-1111-4111-8111-111111111111",
  tenant_id: "22222222-2222-4222-8222-222222222222",
  title: "Backend-Entwicklung",
  description: "Wir bauen Dienste.",
  location: "Berlin",
  postal_code: "10115",
  remote_mode: "hybrid",
  employment_type: "full_time",
  skills: ["Python", "Kubernetes", "Go"],
  status: "published",
  published_at: "2026-08-01T00:00:00Z",
  updated_at: "2026-08-01T00:00:00Z",
};

const SITZUNG = {
  status: "authenticated" as const,
  session: {
    userId: "33333333-3333-4333-8333-333333333333",
    email: "a@b.de",
    tenantId: null, language: "de", displayName: "Anna Beispiel", givenName: "", familyName: "", berufsfeld: null,
  },
};

beforeEach(() => {
  window.localStorage.clear();
});

/**
 * VOR jedem Fall leeren, nicht nur danach.
 *
 * `vi.unstubAllGlobals()` im `afterEach` laeuft, bevor die Komponente
 * ausgehaengt ist. Was die sterbende Seite danach noch anstoesst, trifft den
 * ECHTEN Server — im Entwicklungsrechner den laufenden Stapel, der mit 401
 * antwortet — und schreibt das in den gerade geleerten Zwischenspeicher. Der
 * naechste Fall liest es dann statt seines eigenen Stubs.
 */
beforeEach(() => {
  vergissKontext();
});

afterEach(() => {
  // ERST AUSHAENGEN, DANN DEN STUB ZIEHEN. Andersherum bleibt ein Fenster:
  // die noch montierte Seite stoesst nach `unstubAllGlobals` einen Abruf an,
  // der trifft den ECHTEN Server (auf einem Entwicklungsrechner den laufenden
  // Stapel) und schreibt dessen 401 in den Zwischenspeicher, den der naechste
  // Fall dann liest statt seines eigenen Stubs.
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vergissKontext();
});

/**
 * Eine Antwort in der Seitenform. Die Gesamtzahl folgt aus dem Inhalt, damit
 * ein Test nicht versehentlich eine Blätterleiste behauptet, die es nicht gibt.
 */
function seite(
  items: unknown[],
  page = 1,
  totalItems = items.length,
  pageSize = 12,
  omitted?: number,
) {
  return {
    items,
    page,
    page_size: pageSize,
    total_items: totalItems,
    total_pages: Math.max(1, Math.ceil(totalItems / pageSize)),
    has_next: page * pageSize < totalItems,
    has_previous: page > 1,
    // Fehlt, wenn ohne Umkreis gesucht wurde — genau wie beim Dienst.
    ...(omitted === undefined ? {} : { omitted }),
  };
}

/**
 * Legt eine Ortung in den Browser, die antwortet wie ausgemacht.
 *
 * `jsdom` bringt gar keine mit — ohne dieses Stück ist `"geolocation" in
 * navigator` falsch, der Knopf abgeschaltet und jede Probe darunter grün, ohne
 * je etwas geprüft zu haben.
 */
function stubOrtung(
  antwort:
    | { coords: { latitude: number; longitude: number } }
    | { code: number },
) {
  const getCurrentPosition = vi.fn(
    (
      gelungen: PositionCallback,
      gescheitert?: PositionErrorCallback | null,
    ) => {
      if ("coords" in antwort) {
        gelungen(antwort as unknown as GeolocationPosition);
        return;
      }

      gescheitert?.({
        code: antwort.code,
        message: "",
        PERMISSION_DENIED: 1,
        POSITION_UNAVAILABLE: 2,
        TIMEOUT: 3,
      } as GeolocationPositionError);
    },
  );

  Object.defineProperty(navigator, "geolocation", {
    value: { getCurrentPosition },
    configurable: true,
  });

  return getCurrentPosition;
}

afterEach(() => {
  // @ts-expect-error — in jsdom gibt es die Eigenschaft sonst gar nicht.
  delete navigator.geolocation;
});

/**
 * Ein MUI-Auswahlfeld wird geklickt, nicht `selectOption`-t.
 *
 * Es ist kein `<select>`: MUI zeichnet eine Schaltfläche mit
 * `role="combobox"` und eine Liste mit `role="listbox"`. Dieselbe Hilfe steht
 * in `web/e2e/stack.ts` für die Playwright-Reisen.
 */
async function waehleImFeld(beschriftung: RegExp, eintrag: string) {
  await userEvent.click(screen.getByLabelText(beschriftung));
  await userEvent.click(await screen.findByRole("option", { name: eintrag }));
}

describe("JobsPage", () => {
  it("fragt /jobs und zeigt, was das Unternehmen geschrieben hat", async () => {
    const spion = stubFetch((url) => {
      if (url.includes("/jobs"))
        return { body: seite([STELLE]) };
      if (url.includes("/companies/")) {
        return {
          body: {
            tenant_id: STELLE.tenant_id,
            slug: "acme",
            display_name: "Acme GmbH",
            about: "",
            website: null,
            locations: [],
            benefits: [],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      return { status: 404 };
    });

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("Backend-Entwicklung")).toBeInTheDocument();
    expect(await screen.findByText("Acme GmbH")).toBeInTheDocument();
    expect(
      spion.mock.calls.some(([url]) => String(url).includes("/jobs")),
    ).toBe(true);
  });

  it("schickt leere Filter gar nicht erst mit — ein `remote=` fände nichts", async () => {
    const spion = stubFetch(() => ({ body: seite([]) }));

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.type(screen.getByLabelText(/Suchbegriff/i), "Python");
    await userEvent.click(screen.getByRole("button", { name: /Suchen/i }));

    await waitFor(() => {
      const urls = spion.mock.calls.map(([url]) => String(url));
      expect(urls.some((url) => url.includes("q=Python"))).toBe(true);
    });
    expect(
      spion.mock.calls.every(([url]) => !String(url).includes("remote=")),
    ).toBe(true);
  });

  it("nennt beim leeren Ergebnis, dass nichts gefunden wurde", async () => {
    stubFetch(() => ({ body: seite([]) }));
    renderMitStore(<JobsPage />);
    expect(await screen.findByText(/nichts gefunden/i)).toBeInTheDocument();
  });

  it("zeigt ohne Anmeldung keine Passung — und behauptet damit keine Lücke", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: seite([STELLE]) }
        : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");

    expect(
      screen.queryByText(/von 3 genannten Fähigkeiten/),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
    // Die Fähigkeiten selbst stehen trotzdem da — sie gehören der Anzeige.
    expect(screen.getByText("Python")).toBeInTheDocument();
  });

  it("zählt die Häkchen, nennt aber nie einen Prozentwert", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SITZUNG.session.userId,
            headline: "",
            bio: "",
            location: "",
            remote_ok: false,
            skills: ["python", "Kubernetes"],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      if (url.includes("/jobs"))
        return { body: seite([STELLE]) };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });

    expect(
      await screen.findByText("Du hast 2 von 3 genannten Fähigkeiten:"),
    ).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
    // Gleichheit, nicht Enthaltensein: "Go" bleibt eine Lücke.
    expect(screen.getByText("Go").closest("li")).toHaveAttribute(
      "data-match",
      "missing",
    );
  });

  it("sagt „trage Fähigkeiten ein“ statt „0 von 3“, wenn nichts eingetragen ist", async () => {
    stubFetch((url) => {
      if (url.includes("/profiles/me")) {
        return {
          body: {
            subject_id: SITZUNG.session.userId,
            headline: "",
            bio: "",
            location: "",
            remote_ok: false,
            skills: [],
            updated_at: "2026-08-01T00:00:00Z",
          },
        };
      }
      if (url.includes("/jobs"))
        return { body: seite([STELLE]) };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });

    expect(
      await screen.findByText(/Trage Fähigkeiten in deinem/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/0 von 3/)).not.toBeInTheDocument();
  });

  /*
   * Der Vorgaenger dieses Tests hiess „merkt sich beim Bewerben ohne Konto nur
   * die UUID, nie einen Pfad" und pinnte `merkeStelle`. Die Absicht wurde
   * geschrieben und NIE gelesen: `gemerkteStelle()` hatte ausserhalb von
   * `intent.ts` keinen Aufrufer, und die Anmeldung hatte den Rueckweg
   * ausdruecklich stillgelegt. Eine Absicht, die 24 Stunden im Browser einer
   * Person liegt, ohne dass irgendetwas sie einloest, ist kein halber Komfort,
   * sondern nur der Eintrag. `intent.ts` ist deshalb gefallen (PBI-8.3).
   *
   * Dieser Test haelt fest, was danach gilt — und er ist der Grund, dass eine
   * Rueckkehr eine ENTSCHEIDUNG waere und kein Versehen: wer den Rueckweg
   * bauen will, muss ihn hier zuerst umschreiben.
   */
  it("legt beim Bewerben ohne Konto nichts im Browser ab", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: seite([STELLE]) }
        : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: "Bewerben" }));

    expect(window.localStorage.getItem("wt.gemerkte-stelle")).toBeNull();
    expect(window.localStorage.length).toBe(0);
  });

  it("zeigt die Auswahlkästchen nur nach der Anmeldung", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE]) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    expect(screen.queryByRole("checkbox", { name: /Stelle auswählen/i })).not.toBeInTheDocument();
  });

  /**
   * Die Fortschrittsleiste war GEBAUT und wurde nie erreicht.
   *
   * `setFortschritt` wurde an genau einer Stelle gerufen — im `finally`, mit
   * `null` —, die Leiste las einen Wert, den niemand setzte, und der
   * Katalogschlüssel `stellen.fortschritt` stand in drei Sprachen ohne Leser
   * (PBI-8.1). Dieser Test ist der Grund, dass das nicht wieder unbemerkt
   * abreisst: er verlangt die Zwischenzahl, nicht nur das Ende.
   *
   * Die zweite Stelle HÄNGT mit Absicht. Ohne sie wären beide Entwürfe im
   * selben Augenblick fertig, und „1 von 2" stünde nie auf dem Bildschirm —
   * der Test wäre grün, ohne je einen Fortschritt gesehen zu haben.
   */
  it("zeigt beim Sammelschreiben, wie viele Entwürfe fertig sind", async () => {
    const zweite = {
      ...STELLE,
      id: "55555555-5555-4555-8555-555555555555",
      title: "Zweite Stelle",
    };
    const entwurf = (id: string, jobId: string) => ({
      id,
      job_id: jobId,
      subject: "",
      body: "",
      status: "generating",
      shares_resume: false,
      documents: [],
      comments: [],
      error: null,
      writing_started_at: null,
    });

    let zweitenFreigeben: () => void = () => {};
    const haengt = new Promise<void>((fertig) => {
      zweitenFreigeben = fertig;
    });

    const antwort = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": "application/json" },
      });

    vi.stubGlobal(
      "fetch",
      async (input: string | URL | Request, init?: RequestInit) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.includes("/drafts/zwei/write")) {
          await haengt;
          return antwort(entwurf("zwei", zweite.id));
        }
        if (url.includes("/drafts/eins/write")) {
          return antwort(entwurf("eins", STELLE.id));
        }
        if (url.includes("/applications/drafts")) {
          return init?.method === "POST"
            ? antwort([entwurf("eins", STELLE.id), entwurf("zwei", zweite.id)])
            : antwort([]);
        }
        if (url.includes("/applications/me")) return antwort([]);
        if (url.includes("/jobs")) return antwort(seite([STELLE, zweite]));
        return antwort(null, 404);
      },
    );

    renderMitStore(<JobsPage />, { auth: SITZUNG });
    await screen.findByText("Backend-Entwicklung");

    for (const kasten of await screen.findAllByRole("checkbox", {
      name: /Stelle auswählen/i,
    })) {
      await userEvent.click(kasten);
    }
    await userEvent.click(
      screen.getByRole("button", { name: /Für alle bewerben/i }),
    );

    expect(await screen.findByText("1 von 2 geschrieben")).toBeInTheDocument();

    zweitenFreigeben();
  });

  it("lässt angemeldet mehrere Stellen auswählen", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs")) return { body: seite([STELLE]) };
      if (url.includes("/profiles/me")) return { status: 404 };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });
    await screen.findByText("Backend-Entwicklung");
    const kasten = await screen.findByRole("checkbox", { name: /Stelle auswählen/i });
    await userEvent.click(kasten);
    expect(await screen.findByText(/1 Stelle ausgewählt/i)).toBeInTheDocument();
  });

  /**
   * Eine Seite ERSETZT die vorige, sie hängt nicht an.
   *
   * Das ist der Unterschied zum Vorgänger („mehr laden"), und er ist der Grund
   * für den Umbau: wer auf Seite 2 springt, will Seite 2 sehen und nicht die
   * Seiten 1 und 2 untereinander. Bliebe die erste stehen, wäre die
   * Blätterleiste eine Lüge — sie sagte „13–24 von 26" über eine Liste, die
   * vierundzwanzig Einträge zeigt.
   */
  it("springt auf die zweite Seite und ersetzt die erste", async () => {
    const zweite = {
      ...STELLE,
      id: "44444444-4444-4444-8444-444444444444",
      title: "Zweite Stelle",
    };
    stubFetch((url) => {
      if (!url.includes("/jobs")) return { status: 404 };
      return url.includes("page=2")
        ? { body: seite([zweite], 2, 24, 12) }
        : { body: seite([STELLE], 1, 24, 12) };
    });

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /Seite 2/i }));

    expect(await screen.findByText("Zweite Stelle")).toBeInTheDocument();
    expect(screen.queryByText("Backend-Entwicklung")).not.toBeInTheDocument();
  });

  /**
   * Die Karte zeigt Ort, Arbeitsform und Beschäftigung als WORTE.
   *
   * Der Test, der gefehlt hat. Er prüft nicht, dass irgendetwas dasteht,
   * sondern dass die drei Angaben AUS DEM DRAHT übersetzt ankommen — und dass
   * kein Katalogschlüssel durchschlägt. `stelle.remoteundefined` wäre für
   * `getByText` sonst genauso ein Treffer wie „Hybrid".
   */
  it("zeigt Ort, Arbeitsform und Beschäftigung übersetzt", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE]) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);

    const zeile = await screen.findByText(/Berlin/);
    expect(zeile).toHaveTextContent("Berlin");
    expect(zeile).toHaveTextContent("Hybrid");
    expect(zeile).toHaveTextContent("Vollzeit");
    // Kein roher Schlüssel — weder als „undefined" noch als Punktpfad.
    expect(zeile.textContent ?? "").not.toContain("stelle.");
    expect(zeile.textContent ?? "").not.toContain("undefined");
  });

  /** Die Leiste sagt, wo man ist — und die Zahl kommt vom Server. */
  it("nennt den Bereich und die Gesamtzahl", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE], 1, 26, 12) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("1–12 von 26")).toBeInTheDocument();
  });

  /**
   * <strong>Die Position wird GERUNDET, bevor sie das Haus verlässt.</strong>
   *
   * Die wichtigste Probe dieses Filters, und sie prüft nicht, dass er
   * funktioniert, sondern was er preisgibt. Die Adresszeile steht in jedem
   * Zugriffsprotokoll; ein GPS-Wert mit sieben Nachkommastellen wäre dort die
   * Wohnung. Zwei Stellen sind gut ein Kilometer — und feiner könnte an keiner
   * Antwort etwas ändern, weil die Anzeigen Städte nennen.
   */
  it("rundet den Standort, bevor er in die Adresszeile kommt", async () => {
    const spion = stubFetch((url) =>
      url.includes("/jobs/place")
        ? { body: { location: "Berlin" } }
        : { body: seite([]) },
    );
    const geortet = stubOrtung({
      coords: { latitude: 52.5170365, longitude: 13.3888599 },
    });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    expect(geortet).toHaveBeenCalled();

    await waehleImFeld(/Entfernung/i, "25 km");
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() => {
      const url = spion.mock.calls
        .map(([wert]) => String(wert))
        .find((wert) => wert.includes("radius_km"));

      expect(url).toBeDefined();
      expect(url).toContain("lat=52.52");
      expect(url).toContain("lon=13.39");
      expect(url).toContain("radius_km=25");
      // Der rohe Wert darf nirgends auftauchen — auch nicht abgeschnitten.
      expect(url).not.toContain("52.517");
      expect(url).not.toContain("13.388");
    });
  });

  /**
   * Ohne Standort ist die Entfernung nicht wählbar.
   *
   * Ein Feld, das man bedienen kann und das dann nichts tut, ist schlimmer als
   * ein abgeschaltetes: „25 km" ohne Punkt ist keine Frage, die der Dienst
   * beantworten könnte — er liesse sie stillschweigend fallen, und die Liste
   * sähe aus wie ein Ergebnis.
   */
  it("lässt die Entfernung erst zu, wenn ein Standort da ist", async () => {
    stubFetch(() => ({ body: seite([]) }));
    stubOrtung({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    await waitFor(() =>
      expect(screen.getByLabelText(/Entfernung/i)).not.toHaveAttribute(
        "aria-disabled",
      ),
    );
  });

  /**
   * Ein abgelehnter Zugriff wird beim Namen genannt.
   *
   * Ein gemeinsames „Standort nicht verfügbar" liesse offen, ob die Person
   * selbst abgelehnt hat — dann wäre „nochmal versuchen" schlicht falsch.
   */
  it("sagt, wenn der Standortzugriff abgelehnt wurde", async () => {
    stubFetch(() => ({ body: seite([]) }));
    stubOrtung({ code: 1 });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    expect(await screen.findByText(/abgelehnt/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  /**
   * <strong>Was der Filter nicht beurteilen konnte, steht auf dem Bildschirm.</strong>
   *
   * Eine Umkreissuche kann über eine Anzeige, deren Ort die Ortstabelle nicht
   * kennt, nichts sagen. Sie stumm wegzulassen ergäbe ein Ergebnis, das
   * vollständig aussieht und es nicht ist — die Lüge durch Auslassen aus
   * ADR-0022 §3.
   */
  it("nennt die Anzeigen, über deren Ort nichts bekannt ist", async () => {
    stubFetch((url) =>
      url.includes("/jobs")
        ? { body: seite([STELLE], 1, 1, 12, 3) }
        : { status: 404 },
    );

    renderMitStore(<JobsPage />);

    expect(
      await screen.findByText(/3 Anzeigen nennen keinen Ort, den wir kennen/i),
    ).toBeInTheDocument();
  });

  /** Ohne Umkreissuche gibt es nichts auszulassen — und keinen Hinweis. */
  it("meldet nichts Ausgelassenes, wenn ohne Umkreis gesucht wurde", async () => {
    stubFetch((url) =>
      url.includes("/jobs") ? { body: seite([STELLE]) } : { status: 404 },
    );

    renderMitStore(<JobsPage />);
    await screen.findByText("Backend-Entwicklung");

    expect(
      screen.queryByText(/nennen keinen Ort, den wir kennen/i),
    ).not.toBeInTheDocument();
  });

  /**
   * Standort vergessen nimmt die Entfernung mit.
   *
   * Ein stehengebliebenes „25 km" ohne Punkt sähe wie ein aktiver Filter aus
   * und wäre keiner — die Liste zeigte still wieder alles.
   */
  it("vergisst mit dem Standort auch die gewählte Entfernung", async () => {
    const spion = stubFetch(() => ({ body: seite([]) }));
    stubOrtung({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    await waehleImFeld(/Entfernung/i, "25 km");

    await userEvent.click(
      screen.getByRole("button", { name: /Standort vergessen/i }),
    );
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() =>
      expect(
        spion.mock.calls.every(
          ([url]) => !String(url).includes("radius_km"),
        ),
      ).toBe(true),
    );
    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  /**
   * Bei „vollständig remote" verschwindet die Entfernung.
   *
   * Der Dienst lässt voll remote ausgeschriebene Stellen unabhängig vom Radius
   * durch — die Kombination liefert also dasselbe wie „nur remote" allein. Ein
   * Bedienelement, das sichtbar dasteht und nachweisbar nichts tut, ist
   * schlimmer als keines. Und der schon gewählte Radius geht MIT, sonst zählte
   * er als aktiver Filter und käme beim Zurückschalten unbemerkt wieder.
   */
  it("nimmt bei „vollständig remote“ die Entfernung ganz weg", async () => {
    const spion = stubFetch(() => ({ body: seite([]) }));
    stubOrtung({ coords: { latitude: 52.52, longitude: 13.41 } });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );
    await waehleImFeld(/Entfernung/i, "25 km");

    await waehleImFeld(/Arbeitsform/i, "Vollständig remote");

    expect(screen.queryByLabelText(/Entfernung/i)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    await waitFor(() => {
      const urls = spion.mock.calls.map(([url]) => String(url));
      expect(urls.some((url) => url.includes("remote=full"))).toBe(true);
    });
    expect(
      spion.mock.calls.every(([url]) => !String(url).includes("radius_km")),
    ).toBe(true);

    // Und zurück: das Feld ist wieder da — auf „Egal", nicht auf dem alten Wert.
    await waehleImFeld(/Arbeitsform/i, "Hybrid");
    expect(screen.getByLabelText(/Entfernung/i)).toHaveTextContent("Egal");
  });

  /**
   * <strong>Der gefundene Standort wird SICHTBAR.</strong>
   *
   * Ein Umkreis um einen unsichtbaren Punkt ist eine Zumutung: die Liste ändert
   * sich, und niemand kann sagen, wovon aus gemessen wurde. Der Name kommt aus
   * derselben Tabelle wie die Suche selbst (`/jobs/place`), nicht von einem
   * Fremdanbieter.
   */
  it("trägt den gefundenen Standort in das Ortsfeld ein", async () => {
    stubFetch((url) =>
      url.includes("/jobs/place")
        ? { body: { location: "Berlin" } }
        : { body: seite([]) },
    );
    stubOrtung({ coords: { latitude: 52.5170365, longitude: 13.3888599 } });

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText("Ort")).toHaveValue("");

    await userEvent.click(
      screen.getByRole("button", { name: /Meinen Standort verwenden/i }),
    );

    await waitFor(() =>
      expect(screen.getByLabelText("Ort")).toHaveValue("Berlin"),
    );
  });

  /**
   * Ein getippter Ort ist auch eine Mitte.
   *
   * Vorher hing die Entfernung allein am Ortungsknopf. Wer „Leipzig" tippt und
   * „50 km" will, meint aber „um Leipzig herum" — und braucht dafür weder GPS
   * noch die Erlaubnis dazu.
   */
  it("lässt die Entfernung auch ohne GPS zu, wenn ein Ort dasteht", async () => {
    const spion = stubFetch(() => ({ body: seite([]) }));

    renderMitStore(<JobsPage />);
    await screen.findByText("Dazu wurde nichts gefunden.");

    expect(screen.getByLabelText(/Entfernung/i)).toHaveAttribute(
      "aria-disabled",
      "true",
    );

    await userEvent.type(screen.getByLabelText("Ort"), "Leipzig");

    expect(screen.getByLabelText(/Entfernung/i)).not.toHaveAttribute(
      "aria-disabled",
    );

    await waehleImFeld(/Entfernung/i, "50 km");
    await userEvent.click(screen.getByRole("button", { name: /^Suchen$/i }));

    // Der Ort geht MIT hinaus — der Dienst macht ihn zur Mitte, weil eine
    // Entfernung danebensteht. Koordinaten hat der Browser keine.
    await waitFor(() => {
      const url = spion.mock.calls
        .map(([wert]) => String(wert))
        .find((wert) => wert.includes("radius_km"));

      expect(url).toBeDefined();
      expect(url).toContain("location=Leipzig");
      expect(url).toContain("radius_km=50");
      expect(url).not.toContain("lat=");
    });
  });

  it("zeigt einen gescheiterten Abruf als Fehler, nicht als leere Liste", async () => {
    stubFetch(() => ({
      status: 500,
      body: {
        title: "Kaputt",
        detail: "Es ging schief.",
        correlationId: "abc-123",
      },
    }));

    renderMitStore(<JobsPage />);

    expect(await screen.findByText("Es ging schief.")).toBeInTheDocument();
    expect(
      screen.queryByText("Dazu wurde nichts gefunden."),
    ).not.toBeInTheDocument();
    // Die Korrelationskennung ist der einzige Faden zurück durch alle Dienste.
    expect(screen.getByText(/abc-123/)).toBeInTheDocument();
  });

  it("zeigt bereits beworben statt Bewerben", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: seite([STELLE]) };
      }
      if (url.includes("/applications/me")) {
        return {
          body: [{ id: "a1", job_id: STELLE.id, status: "submitted" }],
        };
      }
      if (url.includes("/applications/drafts")) return { body: [] };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });
    await screen.findByText("Backend-Entwicklung");
    expect(screen.getByRole("button", { name: /Bereits beworben/i })).toBeDisabled();
    expect(screen.queryByRole("link", { name: /^Bewerben$/i })).toBeNull();
  });

  it("zeigt Entwurf öffnen, wenn ein offener Entwurf da ist", async () => {
    stubFetch((url) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: seite([STELLE]) };
      }
      if (url.includes("/applications/me")) return { body: [] };
      if (url.includes("/applications/drafts")) {
        return {
          body: [
            {
              id: "d1",
              job_id: STELLE.id,
              status: "review",
              subject: "",
              body: "",
              version: 1,
              error: "",
              shares_resume: true,
              documents: [],
              comments: [],
              updated_at: "2026-09-07T00:00:00Z",
            },
          ],
        };
      }
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });
    await screen.findByText("Backend-Entwicklung");
    expect(screen.getByRole("link", { name: /Entwurf öffnen/i })).toHaveAttribute(
      "href",
      "/applications/drafts/d1",
    );
    expect(screen.getByRole("checkbox", { name: /Stelle auswählen/i })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /^Bewerben$/i })).toBeNull();
  });

  it("legt beim Bewerben einen Entwurf an und startet das Schreiben", async () => {
    const spion = stubFetch((url, init) => {
      if (url.includes("/jobs/") || url.endsWith("/jobs") || url.includes("/jobs?")) {
        return { body: seite([STELLE]) };
      }
      if (url.includes("/applications/me")) return { body: [] };
      if (init?.method === "POST" && url.includes("/applications/drafts") && !url.includes("/write")) {
        return {
          status: 201,
          body: [
            {
              id: "d-neu",
              job_id: STELLE.id,
              status: "generating",
              subject: "",
              body: "",
              version: 1,
              error: "",
              shares_resume: true,
              documents: [],
              comments: [],
              updated_at: "2026-09-07T00:00:00Z",
            },
          ],
        };
      }
      if (url.includes("/write")) {
        return {
          body: {
            id: "d-neu",
            job_id: STELLE.id,
            status: "generating",
            subject: "",
            body: "",
            version: 1,
            error: "",
            shares_resume: true,
            documents: [],
            comments: [],
            updated_at: "2026-09-07T00:00:00Z",
          },
        };
      }
      if (url.includes("/applications/drafts")) return { body: [] };
      return { status: 404 };
    });

    renderMitStore(<JobsPage />, { auth: SITZUNG });
    await screen.findByText("Backend-Entwicklung");
    await userEvent.click(screen.getByRole("button", { name: /^Bewerben$/i }));

    await waitFor(() => {
      const posts = spion.mock.calls.filter(
        ([, init]) => (init as RequestInit | undefined)?.method === "POST",
      );
      expect(
        posts.some(([url]) => String(url).includes("/applications/drafts") && !String(url).includes("/write")),
      ).toBe(true);
      expect(posts.some(([url]) => String(url).includes("/write"))).toBe(true);
    });
  });
});
