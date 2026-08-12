import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Field, Loading, Page, Select } from "@workertransfer/ui";

import { getCompanyProfile } from "../companies/client";
import { getMyProfile } from "../profile/client";
import { merkeStelle } from "../jobs/intent";
import type { MeResponse } from "../auth/client";

import {
  type EmploymentType,
  type Job,
  type RemoteMode,
  type SearchResult,
  searchJobs,
} from "../jobs/client";
import { Requirements } from "../jobs/Requirements";

import "./jobs.css";

/** Werte aus dem Vertrag sind keine Sätze für Menschen. */
const REMOTE_LABEL: Record<RemoteMode, string> = {
  none: "Vor Ort",
  hybrid: "Hybrid",
  full: "Vollständig remote",
};

const EMPLOYMENT_LABEL: Record<EmploymentType, string> = {
  full_time: "Vollzeit",
  part_time: "Teilzeit",
  contract: "Auf Vertragsbasis",
  internship: "Praktikum",
};

interface Filters {
  q: string;
  location: string;
  remote: RemoteMode | "";
  employment: EmploymentType | "";
}

const EMPTY: Filters = { q: "", location: "", remote: "", employment: "" };

export interface JobsRouteProps {
  // Injizierbar, damit der Test ohne laufende Sitzung rendern kann. `null`
  // heißt „nicht angemeldet" — und die Seite funktioniert dann trotzdem, sie
  // bietet nur kein Bewerben an.
  principal?: MeResponse | null;
}

export function JobsRoute({ principal = null }: JobsRouteProps) {
  // Zwei Zustände: was im Formular steht und wonach gesucht wurde. Sonst
  // liefe bei jedem Tastendruck eine Abfrage.
  const [form, setForm] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);

  const query = useInfiniteQuery<SearchResult>({
    queryKey: ["jobs", applied],
    queryFn: ({ pageParam }) =>
      searchJobs({ ...applied, cursor: pageParam as string | undefined }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.ok ? (last.nextCursor ?? undefined) : undefined),
  });

  // Einmal für die ganze Seite, nicht je Stelle — und derselbe Schlüssel wie
  // auf der Profilseite und der Bewerbungsseite, damit alle drei sich einen
  // Stand teilen.
  const profileQuery = useQuery({
    queryKey: ["profile", "me"],
    queryFn: getMyProfile,
    enabled: principal !== null,
    staleTime: 5 * 60 * 1000,
  });

  // Drei Zustände, und die Unterscheidung trägt:
  //   `null`         — nichts zu vergleichen: nicht angemeldet, oder die
  //                    Antwort steht noch aus. Dann schweigt die Seite dazu,
  //                    statt eine Lücke zu behaupten, die sie nicht kennt.
  //   `[]`           — angemeldet, Antwort da, aber nichts eingetragen (kein
  //                    Profil ODER eines ohne Fähigkeiten). Darüber MUSS die
  //                    Seite sprechen, sonst bliebe sie stumm, wo ein Satz die
  //                    ganze Funktion erklärt.
  //   eine Liste     — abgleichen.
  const mySkills: string[] | null =
    principal === null || !profileQuery.isSuccess ? null : (profileQuery.data?.skills ?? []);

  const pages = query.data?.pages ?? [];
  const failure = pages.find((page) => !page.ok);
  const items: Job[] = pages.flatMap((page) => (page.ok ? page.items : []));

  return (
    <Page
      title="Offene Stellen"
      lead="Was hier steht, haben Unternehmen selbst veröffentlicht. Zum Lesen brauchst du kein Konto — erst zum Bewerben."
    >
      <Card>
        <form
          className="jobs__filters"
          onSubmit={(e) => {
            e.preventDefault();
            setApplied(form);
          }}
        >
          <Field
            label="Suchbegriff"
            placeholder="Python, Pflege, Vertrieb …"
            value={form.q}
            onChange={(e) => setForm({ ...form, q: e.target.value })}
          />
          <Field
            label="Ort"
            value={form.location}
            onChange={(e) => setForm({ ...form, location: e.target.value })}
          />
          <Select
            label="Arbeitsform"
            value={form.remote}
            onChange={(e) => setForm({ ...form, remote: e.target.value as RemoteMode | "" })}
          >
            <option value="">Egal</option>
            <option value="none">Vor Ort</option>
            <option value="hybrid">Hybrid</option>
            <option value="full">Vollständig remote</option>
          </Select>
          {/* Diesen Filter gab es schon: `searchJobs` schickt `employment` seit
              immer mit, nur konnte niemand ihn setzen — er stand im Zustand und
              blieb leer. Ein Wähler dafür ist kein neues Versprechen, sondern
              das Einlösen eines vorhandenen. */}
          <Select
            label="Beschäftigungsart"
            value={form.employment}
            onChange={(e) =>
              setForm({ ...form, employment: e.target.value as EmploymentType | "" })
            }
          >
            <option value="">Egal</option>
            <option value="full_time">Vollzeit</option>
            <option value="part_time">Teilzeit</option>
            <option value="contract">Auf Vertragsbasis</option>
            <option value="internship">Praktikum</option>
          </Select>
          <Button type="submit">Suchen</Button>
        </form>
      </Card>

      {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
          Inhalt. */}
      {query.isPending ? (
        <Card>
          <Loading label="Wird gesucht…" />
        </Card>
      ) : null}

      {failure !== undefined && !failure.ok ? <Alert>{failure.message}</Alert> : null}

      {!query.isPending && failure === undefined && items.length === 0 ? (
        <Empty title="Dazu wurde nichts gefunden." hint="Andere Begriffe führen vielleicht weiter." />
      ) : null}

      {items.length > 0 ? (
        <ul className="candidates">
          {items.map((job) => (
            <li key={job.id}>
              <Card>
                <h2 className="candidates__headline">{job.title}</h2>
                <Hiring tenantId={job.tenant_id} />
                <p className="candidates__meta">
                  {job.location !== "" ? job.location : "Ort nicht angegeben"} ·{" "}
                  {REMOTE_LABEL[job.remote]} · {EMPLOYMENT_LABEL[job.employment]}
                </p>
                <p>{job.description}</p>
                <Requirements skills={job.skills} mine={mySkills} />
                {principal !== null ? (
                  /*
                    Ein LINK auf eine eigene Adresse, kein aufklappendes
                    Formular in der Karte. Das Formular überlebt damit ein
                    Neuladen, ist teilbar, und die gemerkte Absicht nach dem
                    Anmelden hat ein echtes Ziel statt einer Liste, die eine Box
                    aufklappt.
                  */
                  <Button href={`/jobs/${job.id}/apply`}>Bewerben</Button>
                ) : (
                  <>
                    {/*
                      Ein KNOPF, kein Wort in einem Satz. Vorher stand hier
                      „Zum Bewerben anmelden." und nur das letzte Wort war ein
                      Link — für ein Programm anklickbar, für einen Menschen
                      ein Fließtext. Wer bewerben will, sucht einen Knopf, und
                      er muss dasselbe Gewicht haben wie der für Angemeldete;
                      sonst sieht die Seite ohne Konto aus, als könne man hier
                      nichts tun.
                    */}
                    <Button
                      type="button"
                      onClick={() => {
                        // Erst merken, dann wechseln. Der Knopf ist die
                        // EINZIGE Stelle, an der die Absicht entsteht — wer
                        // über die Kopfzeile zur Anmeldung geht, hat keine
                        // geäußert, und dann darf ihn auch nichts irgendwohin
                        // zurückwerfen.
                        merkeStelle(job.id, job.title);
                        window.location.href = "/login";
                      }}
                    >
                      Bewerben
                    </Button>
                    <p className="candidates__meta">
                      Dafür brauchst du ein Konto — danach geht es direkt zur Bewerbung.
                    </p>
                  </>
                )}
              </Card>
            </li>
          ))}
        </ul>
      ) : null}

      {query.hasNextPage ? (
        <Button
          variant="quiet"
          onClick={() => void query.fetchNextPage()}
          disabled={query.isFetchingNextPage}
        >
          {query.isFetchingNextPage ? "Wird geladen…" : "Mehr laden"}
        </Button>
      ) : null}
    </Page>
  );
}

/**
 * Wer sucht.
 *
 * Ohne Profil zeigt die Karte hier nichts — eine Stelle bleibt dann anonym.
 * Das ist ein Zustand, den das Unternehmen selbst herbeigeführt hat, und ihn
 * mit einem Platzhalter wie „Unbekanntes Unternehmen" zu füllen wäre eine
 * Aussage, die niemand gemacht hat.
 *
 * Der Query-Key hängt am Unternehmen, nicht an der Stelle: mehrere Stellen
 * desselben Arbeitgebers teilen sich damit eine Abfrage.
 */
function Hiring({ tenantId }: { tenantId: string }) {
  const query = useQuery({
    queryKey: ["company", "profile", tenantId],
    queryFn: () => getCompanyProfile(tenantId),
    staleTime: 5 * 60 * 1000,
  });

  const profile = query.data;
  if (profile === undefined || profile === null) return null;

  return (
    <p className="jobs__hiring">
      <strong>{profile.display_name}</strong>
      {profile.website !== null ? (
        <>
          {" · "}
          <a href={profile.website} target="_blank" rel="noreferrer noopener">
            Website
          </a>
        </>
      ) : null}
      {profile.benefits.length > 0 ? <> {" · "}{profile.benefits.join(", ")}</> : null}
    </p>
  );
}
