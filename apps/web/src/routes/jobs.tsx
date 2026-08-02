import { useInfiniteQuery, useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card, Field, TextArea } from "@workertransfer/ui";

import { apply } from "../applications/client";
import { getCompanyProfile } from "../companies/client";
import type { MeResponse } from "../auth/client";

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
                <Hiring tenantId={job.tenant_id} />
                <p className="candidates__meta">
                  {job.location !== "" ? job.location : "Ort nicht angegeben"} ·{" "}
                  {REMOTE_LABEL[job.remote]} · {EMPLOYMENT_LABEL[job.employment]}
                </p>
                <p>{job.description}</p>
                {principal !== null ? (
                  <ApplyBox jobId={job.id} />
                ) : (
                  <p className="candidates__meta">
                    Zum Bewerben <a href="/login">anmelden</a>.
                  </p>
                )}
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


/**
 * Bewerben — und damit die eigenen Daten diesem einen Unternehmen freigeben.
 *
 * Die Kästchen benennen, was mitgeht. Das Profil steht bewusst nicht zur Wahl:
 * eine Bewerbung ohne jede Angabe zur Person ist keine, und ein Kästchen dafür
 * wäre eine Wahl, die niemand ernsthaft trifft.
 */
function ApplyBox({ jobId }: { jobId: string }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [resume, setResume] = useState(true);
  const [portfolio, setPortfolio] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const send = useMutation({
    mutationFn: () =>
      apply({
        job_id: jobId,
        message,
        shares_resume: resume,
        shares_portfolio: portfolio,
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSent(true);
        setOpen(false);
      } else {
        setError(result.message);
      }
    },
  });

  if (sent && error === null) {
    return (
      <p className="page__note">
        Bewerbung abgeschickt. Zurückziehen kannst du sie jederzeit unter{" "}
        <a href="/applications">Meine Bewerbungen</a> — dann sieht das Unternehmen deine Daten
        nicht mehr.
      </p>
    );
  }

  if (!open) {
    return (
      <>
        {error !== null ? (
          <p className="auth__alert" role="alert">
            {error}
          </p>
        ) : null}
        <Button variant="quiet" onClick={() => setOpen(true)}>
          Bewerben
        </Button>
      </>
    );
  }

  return (
    <form
      className="jobs__apply"
      onSubmit={(e) => {
        e.preventDefault();
        send.mutate();
      }}
    >
      <TextArea
        label="Anschreiben"
        hint="Optional. Was dich mit dieser Stelle verbindet."
        rows={4}
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        maxLength={4000}
      />
      <p className="wt-field__hint">
        Dein Profil geht immer mit — ohne es wäre es keine Bewerbung. Was du zusätzlich
        freigibst, entscheidest du:
      </p>
      <label className="wt-checkbox">
        <input type="checkbox" checked={resume} onChange={(e) => setResume(e.target.checked)} />
        <span>Lebenslauf</span>
      </label>
      <label className="wt-checkbox">
        <input
          type="checkbox"
          checked={portfolio}
          onChange={(e) => setPortfolio(e.target.checked)}
        />
        <span>Meine Arbeiten</span>
      </label>

      {error !== null ? (
        <p className="auth__alert" role="alert">
          {error}
        </p>
      ) : null}

      <Button type="submit" disabled={send.isPending}>
        {send.isPending ? "Wird gesendet…" : "Bewerbung abschicken"}
      </Button>
      <Button type="button" variant="quiet" onClick={() => setOpen(false)}>
        Abbrechen
      </Button>
    </form>
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
