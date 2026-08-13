import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Empty, Loading, Page, Row, RowList } from "@workertransfer/ui";

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
  // /careers/<kürzel>
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

  const gefunden = company.data?.ok === true ? company.data.profile : undefined;
  const tenantId = gefunden?.tenant_id;
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
      <Page title="Karriere" narrow>
        <Card>
          <Loading label="Unternehmen wird geladen…" />
        </Card>
      </Page>
    );
  }

  // „Gibt es nicht" und „nicht abrufbar" sind zwei verschiedene Sätze. Vorher
  // war beides `null`, und ein Ausfall von companies-service las sich als
  // „diese Firma gibt es nicht" — auf einer Seite, die ein Unternehmen selbst
  // an Bewerber weitergibt.
  if (company.data?.ok === false && company.data.reason === "unavailable") {
    return (
      <Page title="Karriere" narrow>
        <Card>
          <Alert>
            {company.data.message} Das heißt nicht, dass es dieses Unternehmen nicht gibt —
            versuch es später noch einmal.
          </Alert>
        </Card>
      </Page>
    );
  }

  const profile = gefunden;
  if (profile === undefined) {
    return (
      <Page title="Diese Seite gibt es nicht" narrow>
        <Card>
          <p>
            Unter dieser Adresse ist kein Unternehmen hinterlegt.{" "}
            <a href="/jobs">Alle offenen Stellen</a>
          </p>
        </Card>
      </Page>
    );
  }

  const result = jobs.data;
  const items = result?.ok ? result.items : [];
  // Ein GESCHEITERTER Abruf ist kein Leerzustand. Vorher wurde `items` in beiden
  // Fällen leer, und die Seite sagte „Zurzeit ist nichts ausgeschrieben" —
  // also die beruhigendste falsche Antwort, die es gibt. Ein Unternehmen, dessen
  // Stellen gerade nicht abrufbar sind, sieht sonst aus wie eines, das keine
  // hat.
  const abrufFehlgeschlagen = result !== undefined && !result.ok;

  return (
    <Page
      title={profile.display_name}
      narrow
      lead={
        profile.website !== null ? (
          <a href={profile.website} target="_blank" rel="noreferrer noopener">
            {profile.website}
          </a>
        ) : undefined
      }
    >
      {profile.about !== "" ? (
        <Card>
          <h2>Über uns</h2>
          <p>{profile.about}</p>
        </Card>
      ) : null}

      {profile.locations.length > 0 || profile.benefits.length > 0 ? (
        <Card>
          {profile.locations.length > 0 ? <p>Standorte: {profile.locations.join(", ")}</p> : null}
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
        {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
            Inhalt. */}
        {jobs.isPending ? <Loading label="Stellen werden geladen…" /> : null}
        {abrufFehlgeschlagen ? (
          <Alert>
            Die offenen Stellen sind gerade nicht abrufbar. Das heißt nicht, dass es keine gibt —
            versuch es später noch einmal.
          </Alert>
        ) : null}
        {!jobs.isPending && !abrufFehlgeschlagen && items.length === 0 ? (
          <Empty title="Zurzeit ist nichts ausgeschrieben." />
        ) : null}
        {items.length > 0 ? (
          <RowList>
            {items.map((job) => (
              <Row
                key={job.id}
                title={job.title}
                meta={`${job.location !== "" ? job.location : "Ort nicht angegeben"} · ${
                  REMOTE_LABEL[job.remote] ?? job.remote
                }`}
                // Direkt zur Bewerbung dieser Stelle. Vorher schickte diese
                // Seite auf die Stellensuche zurück — jemandem, der die Stelle
                // gerade vor sich hat, zu sagen „such sie dort noch einmal".
                // Möglich wurde es erst, als das Formular eine eigene Adresse
                // bekam; ohne Konto führt sie zur Anmeldung und merkt sich die
                // Stelle, also funktioniert auch der weitergegebene Link.
                actions={<Button href={`/jobs/${job.id}/apply`}>Bewerben</Button>}
              />
            ))}
          </RowList>
        ) : null}
        <p className="wt-field__hint">
          Mit dem Bewerben entsteht die Freigabe deiner Daten — und zwar nur für dieses eine
          Unternehmen.
        </p>
      </Card>
    </Page>
  );
}
