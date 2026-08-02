import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card, Field } from "@workertransfer/ui";

import {
  type EmploymentType,
  type Job,
  type RemoteMode,
  type SearchResult,
  searchJobs,
} from "../jobs/client";

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

export function JobsRoute() {
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

  const pages = query.data?.pages ?? [];
  const failure = pages.find((page) => !page.ok);
  const items: Job[] = pages.flatMap((page) => (page.ok ? page.items : []));

  return (
    <main className="page">
      <header className="page__header">
        <h1>Offene Stellen</h1>
        <p className="page__lead">
          Was hier steht, haben Unternehmen selbst veröffentlicht. Zum Lesen brauchst du kein
          Konto — erst zum Bewerben.
        </p>
      </header>

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
          <label className="wt-field">
            <span className="wt-field__label">Arbeitsform</span>
            <select
              className="wt-field__input"
              value={form.remote}
              onChange={(e) => setForm({ ...form, remote: e.target.value as RemoteMode | "" })}
            >
              <option value="">Egal</option>
              <option value="none">Vor Ort</option>
              <option value="hybrid">Hybrid</option>
              <option value="full">Vollständig remote</option>
            </select>
          </label>
          <Button type="submit">Suchen</Button>
        </form>
      </Card>

      {failure !== undefined && !failure.ok ? (
        <Card>
          <p className="auth__alert" role="alert">
            {failure.message}
          </p>
        </Card>
      ) : null}

      {query.isPending ? <p role="status">Wird gesucht…</p> : null}

      {items.length > 0 ? (
        <ul className="candidates">
          {items.map((job) => (
            <li key={job.id}>
              <Card>
                <h2 className="candidates__headline">{job.title}</h2>
                <p className="candidates__meta">
                  {job.location !== "" ? job.location : "Ort nicht angegeben"} ·{" "}
                  {REMOTE_LABEL[job.remote]} · {EMPLOYMENT_LABEL[job.employment]}
                </p>
                <p>{job.description}</p>
              </Card>
            </li>
          ))}
        </ul>
      ) : null}

      {!query.isPending && failure === undefined && items.length === 0 ? (
        <Card>
          <p>Dazu wurde nichts gefunden. Andere Begriffe führen vielleicht weiter.</p>
        </Card>
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
    </main>
  );
}
