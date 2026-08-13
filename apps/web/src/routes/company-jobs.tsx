import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Loading, Page, Row, RowList } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { type Job, closeJob, listOwnJobs, publishJob } from "../jobs/client";

export interface CompanyJobsRouteProps {
  principal?: MeResponse | null;
}

const STATUS_LABEL: Record<Job["status"], string> = {
  draft: "Entwurf — sieht nur ihr",
  published: "Veröffentlicht",
  closed: "Geschlossen",
};

export function CompanyJobsRoute({ principal = null }: CompanyJobsRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;

  const jobsQuery = useQuery({
    queryKey: ["jobs", "own", tenantId],
    queryFn: listOwnJobs,
    enabled: tenantId !== null,
  });

  const [error, setError] = useState<string | null>(null);

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ["jobs", "own", tenantId] });
  }

  const transition = useMutation({
    mutationFn: ({ id, publish }: { id: string; publish: boolean }) =>
      publish ? publishJob(id) : closeJob(id),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      refresh();
    },
  });

  if (tenantId === null) {
    return (
      <Page title="Unsere Stellen" narrow>
        <Card>
          <p>
            Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen
            einladen.
          </p>
        </Card>
      </Page>
    );
  }

  const jobs = jobsQuery.data;

  return (
    <Page
      title="Unsere Stellen"
      narrow
      lead="Ein Entwurf sieht niemand außer euch. Veröffentlicht ist er für alle sichtbar, auch ohne Konto — und geschlossen bleibt geschlossen."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      <Card>
        {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
            Inhalt. Der LADEZUSTAND fehlte — `isPending` wurde gar nicht
            geprüft, und weil auch der Leersatz `jobs?.ok` verlangt, blieb die
            Karte währenddessen vollständig leer. */}
        {jobsQuery.isPending ? <Loading label="Stellen werden geladen…" /> : null}
        {jobs !== undefined && !jobs.ok ? <Alert>{jobs.message}</Alert> : null}
        {jobs?.ok && jobs.jobs.length === 0 ? (
          <Empty
            title="Noch keine Stelle angelegt."
            action={<Button href="/company/jobs/new">Neue Stelle</Button>}
          />
        ) : null}
        {jobs?.ok && jobs.jobs.length > 0 ? (
          <>
            <RowList>
              {jobs.jobs.map((job) => (
                <Row
                  key={job.id}
                  title={job.title}
                  meta={STATUS_LABEL[job.status]}
                  // Für eine geschlossene Stelle gibt es gar nichts mehr — es
                  // gibt keinen Weg zurück, und ein Knopf würde einen behaupten.
                  actions={
                    job.status === "closed" ? undefined : (
                      <>
                        {job.status === "draft" ? (
                          <Button
                            onClick={() => transition.mutate({ id: job.id, publish: true })}
                            disabled={transition.isPending}
                          >
                            Veröffentlichen
                          </Button>
                        ) : null}
                        <Button
                          variant="quiet"
                          onClick={() => transition.mutate({ id: job.id, publish: false })}
                          disabled={transition.isPending}
                        >
                          Schließen
                        </Button>
                      </>
                    )
                  }
                />
              ))}
            </RowList>
            <Button href="/company/jobs/new">Neue Stelle</Button>
          </>
        ) : null}
      </Card>
    </Page>
  );
}
