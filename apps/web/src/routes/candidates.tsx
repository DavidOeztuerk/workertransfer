import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { type CandidatePage, type Profile, listCandidates } from "../profile/client";
import {
  type MarketRequest,
  getMarketStatus,
  listCompanyMarketRequests,
  requestMarketStatus,
} from "../market/client";
import { requestResume } from "../resume/client";
import { expressInterest } from "../transfers/client";

export interface CandidatesRouteProps {
  principal?: MeResponse | null;
}

export function CandidatesRoute({ principal = null }: CandidatesRouteProps) {
  // Ohne aktives Unternehmen wird gar nicht erst gefragt. Der Server würde 403
  // antworten — aber eine Anfrage, deren Ergebnis feststeht, ist nur Rauschen
  // in den Logs des Ledgers.
  const hasCompany = principal?.tenant_id != null;

  const query = useInfiniteQuery<CandidatePage>({
    queryKey: ["candidates", principal?.tenant_id ?? null],
    queryFn: ({ pageParam }) => listCandidates(pageParam as string | undefined),
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
      <main className="page page--narrow">
        <Card>
          <h1>Kandidatinnen und Kandidaten</h1>
          <p>
            Profile sehen nur Unternehmen. Wechsle oben auf ein Unternehmen — oder{" "}
            <a href="/company/new">lege eines an</a>.
          </p>
        </Card>
      </main>
    );
  }

  const pages = query.data?.pages ?? [];
  // Der erste Fehler gewinnt: steht er auf Seite 3, bleiben die Seiten 1 und 2
  // trotzdem stehen — sie waren echt.
  const failure = pages.find((page) => !page.ok);
  const items: Profile[] = pages.flatMap((page) => (page.ok ? page.items : []));

  return (
    <main className="page">
      <header className="page__header">
        <h1>Kandidatinnen und Kandidaten</h1>
        <p className="page__lead">
          Hier steht ausschließlich, wer sein Profil freigegeben hat. Wer die Freigabe zurückzieht,
          verschwindet beim nächsten Laden — ohne Umweg über uns.
        </p>
      </header>

      {failure !== undefined && !failure.ok ? (
        <Card>
          <p className="auth__alert" role="alert">
            {failure.message}
          </p>
        </Card>
      ) : null}

      {query.isPending ? <p role="status">Wird geladen…</p> : null}

      {items.length > 0 ? (
        <ul className="candidates">
          {items.map((profile) => (
            <li key={profile.subject_id}>
              <Card>
                <h2 className="candidates__headline">{profile.headline}</h2>
                <p className="candidates__meta">
                  {profile.location !== "" ? profile.location : "Ort nicht angegeben"}
                  {profile.remote_ok ? " · Remote möglich" : null}
                </p>
                {profile.bio !== "" ? <p>{profile.bio}</p> : null}
                {profile.skills.length > 0 ? (
                  <ul className="candidates__skills">
                    {profile.skills.map((skill) => (
                      <li key={skill}>{skill}</li>
                    ))}
                  </ul>
                ) : null}
                <ResumeRequestButton subjectId={profile.subject_id} />
                <MarketAccess
                  subjectId={profile.subject_id}
                  request={marketRequests.get(profile.subject_id)}
                />
              </Card>
            </li>
          ))}
        </ul>
      ) : null}

      {!query.isPending && failure === undefined && items.length === 0 ? (
        <Card>
          <p>
            Im Moment hat niemand sein Profil freigegeben. Das ist kein Fehler — es ist die
            Voreinstellung.
          </p>
        </Card>
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
    </main>
  );
}


/**
 * Nach dem Lebenslauf fragen.
 *
 * Die Anfrage nennt nur die Subject-ID — welches Unternehmen fragt, steht im
 * Token, und welche Berechtigung daraus folgt, entscheidet der Server. Die
 * Oberfläche baut nie einen Capability-String.
 */
function ResumeRequestButton({ subjectId }: { subjectId: string }) {
  const [message, setMessage] = useState<string | null>(null);
  const [asked, setAsked] = useState(false);

  const ask = useMutation({
    mutationFn: () => requestResume(subjectId),
    onSuccess: (result) => {
      if (result.ok) {
        setMessage(null);
        setAsked(true);
      } else {
        // Auch "schon gefragt" ist ein Ergebnis, kein Fehler der Oberfläche —
        // es bleibt stehen, statt den Knopf einfach wieder anzubieten.
        setMessage(result.message);
        setAsked(result.reason === "already-asked");
      }
    },
  });

  if (asked && message === null) {
    return <p className="candidates__asked">Anfrage gestellt. Die Person entscheidet.</p>;
  }

  return (
    <>
      {message !== null ? (
        <p className="auth__alert" role="alert">
          {message}
        </p>
      ) : null}
      {!asked ? (
        <Button variant="quiet" onClick={() => ask.mutate()} disabled={ask.isPending}>
          {ask.isPending ? "Wird gefragt…" : "Lebenslauf anfragen"}
        </Button>
      ) : null}
    </>
  );
}


/**
 * Die zweite Tür: „darf ich sehen, ob du gerade zuhörst?"
 *
 * Getrennt von der Lebenslauf-Freigabe, und das ist Absicht. Ein Lebenslauf
 * verrät, wo jemand war; der Marktstatus verrät, dass er weg will. Ein
 * Schalter, der beim Umlegen das Zweite mitfreigibt, wäre genau der Schalter,
 * dessen Folgen niemand überblickt.
 */
function MarketAccess({
  subjectId,
  request,
}: {
  subjectId: string;
  request: MarketRequest | undefined;
}) {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);

  const granted = request?.status === "GRANTED";
  const statusQuery = useQuery({
    queryKey: ["market", "status", subjectId],
    queryFn: () => getMarketStatus(subjectId),
    // Nur fragen, wenn einmal freigegeben wurde. Sonst wäre jede Kartenansicht
    // eine Prüfung im Ledger, deren Antwort ohnehin feststeht.
    enabled: granted,
  });

  const ask = useMutation({
    mutationFn: () => requestMarketStatus(subjectId),
    onSuccess: (result) => {
      setMessage(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["market", "requests", "company"] });
    },
  });

  const interest = useMutation({
    mutationFn: () => expressInterest(subjectId, "Wir würden gern mit dir sprechen."),
    onSuccess: (result) => {
      setMessage(result.ok ? "Interesse hinterlegt. Die Person entscheidet." : result.message);
      void queryClient.invalidateQueries({ queryKey: ["transfers", "company"] });
    },
  });

  const status = statusQuery.data?.ok === true ? statusQuery.data.status : null;

  return (
    <div className="candidates__market">
      {message !== null ? (
        <p className="auth__alert" role="alert">
          {message}
        </p>
      ) : null}

      {request === undefined ? (
        <Button variant="quiet" onClick={() => ask.mutate()} disabled={ask.isPending}>
          {ask.isPending ? "Wird gefragt…" : "Marktstatus anfragen"}
        </Button>
      ) : null}

      {request?.status === "PENDING" ? (
        <p className="candidates__asked">Marktstatus angefragt. Die Person entscheidet.</p>
      ) : null}

      {request?.status === "DECLINED" ? (
        <p className="candidates__asked">Marktstatus abgelehnt.</p>
      ) : null}

      {granted && status === null && !statusQuery.isPending ? (
        // „Zurückgezogen", „gibt es nicht" und „gerade nicht" sind vom Server
        // ununterscheidbar gehalten. Die Oberfläche bastelt daraus keine
        // Auskunft, die er gerade verweigert hat.
        <p className="candidates__asked">Marktstatus gerade nicht einsehbar.</p>
      ) : null}

      {status !== null ? (
        <>
          <p className="candidates__asked">
            {AVAILABILITY[status.availability]}
            {status.employed ? " · arbeitet gerade" : null}
          </p>
          {status.note !== "" ? <p>{status.note}</p> : null}
          {status.is_approachable ? (
            <Button onClick={() => interest.mutate()} disabled={interest.isPending}>
              {interest.isPending ? "Wird hinterlegt…" : "Interesse zeigen"}
            </Button>
          ) : null}
        </>
      ) : null}
    </div>
  );
}

const AVAILABILITY: Record<string, string> = {
  open: "Sucht aktiv",
  listening: "Hört zu",
  unavailable: "Gerade nicht ansprechbar",
};
