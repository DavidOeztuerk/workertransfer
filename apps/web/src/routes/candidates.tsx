import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Empty,
  Field,
  Loading,
  Page,
} from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type CandidateFilters,
  type CandidatePage,
  NO_FILTERS,
  type Profile,
  listCandidates,
} from "../profile/client";
import { type MarketRequest, listCompanyMarketRequests } from "../market/client";
import { CandidateCard } from "../candidates/CandidateCard";

export interface CandidatesRouteProps {
  principal?: MeResponse | null;
}

export function CandidatesRoute({ principal = null }: CandidatesRouteProps) {
  // Ohne aktives Unternehmen wird gar nicht erst gefragt. Der Server würde 403
  // antworten — aber eine Anfrage, deren Ergebnis feststeht, ist nur Rauschen
  // in den Logs des Ledgers.
  const hasCompany = principal?.tenant_id != null;

  // Zwei Zustände, mit Absicht: was im Formular steht, und wonach gerade
  // gesucht wird. Sonst liefe bei jedem Tastendruck eine Abfrage — und die
  // Liste flackerte, während jemand ein Wort tippt.
  const [draft, setDraft] = useState<{ skills: string; location: string; remoteOnly: boolean }>({
    skills: "",
    location: "",
    remoteOnly: false,
  });
  const [filters, setFilters] = useState<CandidateFilters>(NO_FILTERS);
  const hasFilters =
    filters.skills.length > 0 || filters.location !== "" || filters.remoteOnly;

  const query = useInfiniteQuery<CandidatePage>({
    queryKey: ["candidates", principal?.tenant_id ?? null, filters],
    queryFn: ({ pageParam }) => listCandidates(pageParam as string | undefined, filters),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.ok ? (last.nextCursor ?? undefined) : undefined),
    enabled: hasCompany,
  });

  // Eine Abfrage für die ganze Seite statt einer je Karte: der Ledger sähe
  // sonst bei jedem Seitenaufruf eine Prüfung zu jeder Person — und die meisten
  // hätten dieselbe Antwort „nie gefragt".
  const requestsQuery = useQuery({
    queryKey: ["market", "requests", "company", principal?.tenant_id ?? null],
    queryFn: listCompanyMarketRequests,
    enabled: hasCompany,
  });
  const marketRequests = new Map<string, MarketRequest>(
    requestsQuery.data?.ok === true
      ? requestsQuery.data.requests.map((request) => [request.subject_id, request])
      : []
  );

  if (!hasCompany) {
    return (
      <Page title="Kandidatinnen und Kandidaten" narrow>
        <Card>
          <p>
            Profile sehen nur Unternehmen. Wechsle oben auf ein Unternehmen — oder lass dich von
            jemandem aus deinem Unternehmen einladen.
          </p>
        </Card>
      </Page>
    );
  }

  const pages = query.data?.pages ?? [];
  // Der erste Fehler gewinnt: steht er auf Seite 3, bleiben die Seiten 1 und 2
  // trotzdem stehen — sie waren echt.
  const failure = pages.find((page) => !page.ok);
  const items: Profile[] = pages.flatMap((page) => (page.ok ? page.items : []));

  return (
    <Page
      title="Kandidatinnen und Kandidaten"
      lead="Hier steht ausschließlich, wer sein Profil freigegeben hat. Wer die Freigabe zurückzieht, verschwindet beim nächsten Laden — ohne Umweg über uns."
    >
      {failure !== undefined && !failure.ok ? <Alert>{failure.message}</Alert> : null}

      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setFilters({
              skills: draft.skills.split(",").map((entry) => entry.trim()),
              location: draft.location,
              remoteOnly: draft.remoteOnly,
            });
          }}
        >
          <Field
            label="Fähigkeiten"
            hint="Mit Komma trennen. Es zählt, wer ALLE davon kann."
            placeholder="Python, Kubernetes"
            value={draft.skills}
            onChange={(e) => setDraft((current) => ({ ...current, skills: e.target.value }))}
          />
          <Field
            label="Ort"
            hint="Ein Teil genügt."
            placeholder="Berlin"
            value={draft.location}
            onChange={(e) => setDraft((current) => ({ ...current, location: e.target.value }))}
          />
          {/* Ein Kästchen, kein Schalter: der Filter gilt mit dem Absenden, und
              genau das versprechen die beiden Elemente unterschiedlich. */}
          <Checkbox
            label="Nur wer Remote angegeben hat"
            hint={
              <>
                Ohne Haken erscheinen alle. Es gibt keinen Filter für „nur vor Ort" — ein fehlender
                Haken heißt „nicht ja gesagt", nicht „lehnt ab".
              </>
            }
            checked={draft.remoteOnly}
            onChange={(e) => setDraft((current) => ({ ...current, remoteOnly: e.target.checked }))}
          />
          <Button type="submit">Suchen</Button>
          {hasFilters ? (
            <Button
              type="button"
              variant="quiet"
              onClick={() => {
                setDraft({ skills: "", location: "", remoteOnly: false });
                setFilters(NO_FILTERS);
              }}
            >
              Filter zurücksetzen
            </Button>
          ) : null}
        </form>
      </Card>

      {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
          Inhalt. Der Fehler steht schon oben — er gilt für die ganze Liste. */}
      {query.isPending ? (
        <Card>
          <Loading label="Profile werden geladen…" />
        </Card>
      ) : null}

      {items.length > 0 ? (
        <ul className="candidates">
          {items.map((profile) => (
            <li key={profile.subject_id}>
              <CandidateCard
                profile={profile}
                marketRequest={marketRequests.get(profile.subject_id)}
              />
            </li>
          ))}
        </ul>
      ) : null}

      {!query.isPending && failure === undefined && items.length === 0 ? (
        // Eine leere Trefferliste sagt etwas über die SUCHE, nicht über die
        // Plattform. Beides zu vermischen hieße, aus „niemand passt" ein „hier
        // ist niemand" zu machen — deshalb zwei Sätze und nicht einer.
        hasFilters ? (
          <Empty title="Auf diese Suche passt gerade niemand, der sein Profil freigegeben hat." />
        ) : (
          <Empty
            title="Im Moment hat niemand sein Profil freigegeben."
            hint="Das ist kein Fehler — es ist die Voreinstellung."
          />
        )
      ) : null}

      {/* Bewusst keine Gesamtzahl: sie würde verraten, wie viele Profile es
          gibt, die gerade NICHT freigegeben sind. */}
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
