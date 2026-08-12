import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card } from "@workertransfer/ui";

import { getGitHub } from "../github/client";
import { type MarketRequest, getMarketStatus, requestMarketStatus } from "../market/client";
import type { Profile } from "../profile/client";
import { requestResume } from "../resume/client";
import { expressInterest } from "../transfers/client";

import "./candidate-card.css";

/**
 * Eine Person, wie ein Unternehmen sie sieht — und die drei Türen daneben.
 *
 * Liegt in einer eigenen Datei, weil hier der Umfang der Seite steckt, nicht in
 * der Liste: die Karte selbst ist Text, aber jede der drei Türen führt ein
 * eigenes Gespräch mit einem eigenen Dienst und hat eigene Zustände — gefragt,
 * gewährt, abgelehnt, still. Die Route darüber tut dagegen nur zwei Dinge,
 * suchen und auflisten.
 *
 * Die drei sind ausdrücklich NICHT zu einer zusammengefasst. Ein Lebenslauf
 * verrät, wo jemand war; der Marktstatus, dass er weg will; die GitHub-Belege,
 * was er öffentlich getan hat. Ein Knopf, der alle drei aufmacht, wäre genau
 * der Knopf, dessen Folgen niemand überblickt.
 */
export function CandidateCard({
  profile,
  marketRequest,
}: {
  profile: Profile;
  marketRequest: MarketRequest | undefined;
}) {
  return (
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
      <MarketAccess subjectId={profile.subject_id} request={marketRequest} />
      <GitHubEvidence subjectId={profile.subject_id} />
    </Card>
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
      {message !== null ? <Alert>{message}</Alert> : null}
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
      {message !== null ? <Alert>{message}</Alert> : null}

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


/**
 * Die GitHub-Belege einer Person — wenn sie freigegeben sind.
 *
 * Kein „nicht verbunden" und kein „nicht freigegeben": beides ist von „gibt es
 * nicht" ununterscheidbar, genau wie der Server es hält. Wer nichts auf GitHub
 * hat, ist nicht schlechter, sondern woanders (ADR-0022) — deshalb steht hier
 * lieber gar nichts als ein Hinweis auf eine Leerstelle.
 */
function GitHubEvidence({ subjectId }: { subjectId: string }) {
  const query = useQuery({
    queryKey: ["github", subjectId],
    queryFn: () => getGitHub(subjectId),
  });

  const connection = query.data?.ok === true ? query.data.connection : null;
  if (connection === null) return null;

  return (
    <div className="candidates__github">
      <p className="requests__title">
        <a href={`https://github.com/${connection.login}`} target="_blank" rel="noreferrer noopener">
          github.com/{connection.login}
        </a>
      </p>
      {connection.repositories.length === 0 ? (
        <p className="requests__meta">Keine öffentlichen Repositories.</p>
      ) : (
        <ul className="candidates__repos">
          {/* Die ersten fünf, nach letzter Änderung. Keine Auswahl nach
              „Qualität" — die gibt es hier nicht, und eine Reihung wäre bereits
              eine Wertung. */}
          {connection.repositories.slice(0, 5).map((repo) => (
            <li key={repo.name}>
              <a href={repo.url} target="_blank" rel="noreferrer noopener">
                {repo.name}
              </a>
              {repo.language !== null ? ` · ${repo.language}` : null}
            </li>
          ))}
        </ul>
      )}
      <p className="requests__meta">
        {connection.fetched_at !== null
          ? `Stand: ${new Date(connection.fetched_at).toLocaleDateString("de-DE")}`
          : null}
      </p>
    </div>
  );
}
