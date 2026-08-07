import { useQuery } from "@tanstack/react-query";
import { Card } from "@workertransfer/ui";

import { getCompanyBySlug } from "../companies/client";
import { searchJobs } from "../jobs/client";

export interface CareerRouteProps {
  /** Injizierbar für den Test; sonst aus dem Pfad. */
  slug?: string;
}

const REMOTE_LABEL: Record<string, string> = {
  none: "Vor Ort",
  hybrid: "Hybrid",
  full: "Vollständig remote",
};

function slugFromPath(): string {
  // /karriere/<kürzel>
  const parts = window.location.pathname.split("/").filter(Boolean);
  return parts[1] ?? "";
}

/**
 * Die Karriere-Seite eines Unternehmens.
 *
 * Pfadbasiert, nicht als Subdomain: eine Subdomain ist eine Betriebsfrage
 * (Wildcard-DNS, Proxy, Zertifikat) und steht in keinem Anwendungscode.
 * Derselbe Code läuft später hinter `karriere.firma.de`, wenn ein Proxy den
 * Host auf diesen Pfad umschreibt.
 *
 * Kein Konto nötig — sie ist zum Teilen gedacht.
 */
export function CareerRoute({ slug }: CareerRouteProps) {
  const wanted = slug ?? slugFromPath();

  const company = useQuery({
    queryKey: ["career", wanted],
    queryFn: () => getCompanyBySlug(wanted),
    enabled: wanted !== "",
  });

  const tenantId = company.data?.tenant_id;
  const jobs = useQuery({
    // Zwei Aufrufe statt eines zusammengesetzten Endpunkts: die Dienste haben
    // getrennte Datenbanken, und einer, der für den anderen antwortet, verwischt
    // genau diese Grenze.
    queryKey: ["career", "jobs", tenantId],
    queryFn: () => searchJobs({ company: tenantId as string, limit: 50 }),
    enabled: tenantId !== undefined,
  });

  if (company.isPending) {
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Wird geladen…</p>
        </Card>
      </main>
    );
  }

  const profile = company.data;
  if (profile === null || profile === undefined) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Diese Seite gibt es nicht</h1>
          <p>
            Unter dieser Adresse ist kein Unternehmen hinterlegt.{" "}
            <a href="/jobs">Alle offenen Stellen</a>
          </p>
        </Card>
      </main>
    );
  }

  const result = jobs.data;
  const items = result?.ok ? result.items : [];

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>{profile.display_name}</h1>
        {profile.website !== null ? (
          <p className="page__lead">
            <a href={profile.website} target="_blank" rel="noreferrer noopener">
              {profile.website}
            </a>
          </p>
        ) : null}
      </header>

      {profile.about !== "" ? (
        <Card>
          <h2>Über uns</h2>
          <p>{profile.about}</p>
        </Card>
      ) : null}

      {profile.locations.length > 0 || profile.benefits.length > 0 ? (
        <Card>
          {profile.locations.length > 0 ? (
            <p className="candidates__meta">Standorte: {profile.locations.join(", ")}</p>
          ) : null}
          {profile.benefits.length > 0 ? (
            <ul className="candidates__skills">
              {profile.benefits.map((benefit) => (
                <li key={benefit}>{benefit}</li>
              ))}
            </ul>
          ) : null}
        </Card>
      ) : null}

      <Card>
        <h2>Offene Stellen</h2>
        {jobs.isPending ? <p role="status">Stellen werden geladen…</p> : null}
        {!jobs.isPending && items.length === 0 ? (
          <p>Zurzeit ist nichts ausgeschrieben.</p>
        ) : null}
        {items.length > 0 ? (
          <ul className="team">
            {items.map((job) => (
              <li key={job.id}>
                <span>{job.title}</span>
                <span className="team__role">
                  {job.location !== "" ? job.location : "Ort nicht angegeben"} ·{" "}
                  {REMOTE_LABEL[job.remote] ?? job.remote}
                </span>
              </li>
            ))}
          </ul>
        ) : null}
        <p className="wt-field__hint">
          Bewerben geht über <a href="/jobs">die Stellensuche</a> — dort entsteht die Freigabe
          deiner Daten, und zwar nur für dieses eine Unternehmen.
        </p>
      </Card>
    </main>
  );
}
