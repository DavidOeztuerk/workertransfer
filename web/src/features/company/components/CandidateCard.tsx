import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";

import { useAsync } from "../lib/useAsync";
import type { Profile } from "../api/candidates";
import { getGitHub } from "../api/github";
import { requestResume } from "../api/resumeRequests";
import {
  type MarketRequest,
  getMarketStatus,
  requestMarketStatus,
} from "../../work/api/market";
import { expressInterest } from "../../work/api/transfers";

const ANSPRECHBARKEIT: Record<string, string> = {
  open: "Sucht aktiv",
  listening: "Hört zu",
  unavailable: "Gerade nicht ansprechbar",
};

/**
 * Eine Kandidatin, wie ein Unternehmen sie sieht.
 *
 * <strong>Es steht keine Zahl auf dieser Karte, und es darf keine geben.</strong>
 * Kein Punktwert, keine Passung in Prozent, keine Rangfolge. Was hier steht,
 * hat die Person selbst geschrieben oder ausdrücklich freigegeben: Überschrift,
 * Ort, Text, Fähigkeiten — und Belege mit Herkunft, wo sie GitHub verbunden hat.
 *
 * <strong>Drei getrennte Türen, und keine öffnet die andere.</strong> Der
 * Lebenslauf wird angefragt, der Marktstatus wird angefragt, das Interesse wird
 * hinterlegt. Jede Anfrage beantwortet die Person einzeln.
 */
export function CandidateCard({
  profile,
  marketRequest,
  onGeaendert,
}: {
  profile: Profile;
  marketRequest: MarketRequest | undefined;
  onGeaendert: () => void;
}) {
  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h3" sx={{ mb: 0.5 }}>
          {profile.headline}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {profile.location !== "" ? profile.location : "Ort nicht angegeben"}
          {profile.remote_ok ? " · Remote möglich" : null}
        </Typography>

        {profile.bio !== "" ? (
          <Typography sx={{ mb: 1.5 }}>{profile.bio}</Typography>
        ) : null}

        {profile.skills.length > 0 ? (
          <Box sx={{ display: "flex", gap: 0.75, flexWrap: "wrap", mb: 2 }}>
            {profile.skills.map((faehigkeit) => (
              <Chip key={faehigkeit} label={faehigkeit} size="small" />
            ))}
          </Box>
        ) : null}

        <Lebenslaufanfrage subjectId={profile.subject_id} />
        <Marktzugang
          subjectId={profile.subject_id}
          anfrage={marketRequest}
          onGeaendert={onGeaendert}
        />
        <GitHubBelege subjectId={profile.subject_id} />
      </CardContent>
    </Card>
  );
}

/**
 * Den Lebenslauf anfragen.
 *
 * <strong>Fragen darf nur, wer die Profil-Freigabe hat</strong> — und ob
 * überhaupt ein Lebenslauf existiert, verrät diese Tür nicht. „Hat schon einen
 * geschrieben" wäre eine Tatsache über die Person, nach der niemand tasten
 * können soll.
 */
function Lebenslaufanfrage({ subjectId }: { subjectId: string }) {
  const [meldung, setMeldung] = useState<string | null>(null);
  const [gefragt, setGefragt] = useState(false);
  const [laeuft, setLaeuft] = useState(false);

  if (gefragt) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Anfrage gestellt. Die Person entscheidet.
      </Typography>
    );
  }

  return (
    <Box sx={{ mb: 1 }}>
      {meldung !== null ? (
        <Alert severity="warning" sx={{ mb: 1 }}>
          {meldung}
        </Alert>
      ) : null}
      <Button
        variant="text"
        size="small"
        disabled={laeuft}
        onClick={() => {
          setLaeuft(true);
          void requestResume(subjectId).then((ergebnis) => {
            setLaeuft(false);
            if (ergebnis.ok) {
              setMeldung(null);
              setGefragt(true);
            } else {
              setMeldung(ergebnis.error.detail);
            }
          });
        }}
      >
        {laeuft ? "Wird gefragt…" : "Lebenslauf anfragen"}
      </Button>
    </Box>
  );
}

/**
 * Marktstatus anfragen, und — erst nach Freigabe — Interesse hinterlegen.
 *
 * <strong>„Freigegeben" heisst nicht „einsehbar".</strong> Der Ledger kann im
 * selben Augenblick schweigen; dann steht hier ein eigener Satz statt eines
 * erfundenen Status. Und Interesse lässt sich nur zeigen, wenn die Person
 * gerade <em>ansprechbar</em> ist — die Freigabe allein reicht nicht.
 */
function Marktzugang({
  subjectId,
  anfrage,
  onGeaendert,
}: {
  subjectId: string;
  anfrage: MarketRequest | undefined;
  onGeaendert: () => void;
}) {
  const [meldung, setMeldung] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const erteilt = anfrage?.status === "GRANTED";
  const stand = useAsync(
    (signal) => getMarketStatus(subjectId, signal),
    [subjectId],
    erteilt,
  );

  const status = stand.data?.ok === true ? stand.data.status : null;

  return (
    <Box sx={{ mb: 1 }}>
      {meldung !== null ? (
        <Alert severity="info" sx={{ mb: 1 }}>
          {meldung}
        </Alert>
      ) : null}

      {anfrage === undefined ? (
        <Button
          variant="text"
          size="small"
          disabled={laeuft}
          onClick={() => {
            setLaeuft(true);
            void requestMarketStatus(subjectId).then((ergebnis) => {
              setLaeuft(false);
              setMeldung(ergebnis.ok ? null : ergebnis.error.detail);
              onGeaendert();
            });
          }}
        >
          {laeuft ? "Wird gefragt…" : "Marktstatus anfragen"}
        </Button>
      ) : null}

      {anfrage?.status === "PENDING" ? (
        <Typography variant="body2" color="text.secondary">
          Marktstatus angefragt. Die Person entscheidet.
        </Typography>
      ) : null}

      {anfrage?.status === "DECLINED" ? (
        <Typography variant="body2" color="text.secondary">
          Marktstatus abgelehnt.
        </Typography>
      ) : null}

      {erteilt && status === null && !stand.pending ? (
        <Typography variant="body2" color="text.secondary">
          Marktstatus gerade nicht einsehbar.
        </Typography>
      ) : null}

      {status !== null ? (
        <>
          <Typography variant="body2" color="text.secondary">
            {ANSPRECHBARKEIT[status.availability]}
            {status.employed ? " · arbeitet gerade" : null}
          </Typography>
          {status.note !== "" ? (
            <Typography sx={{ mt: 0.5 }}>{status.note}</Typography>
          ) : null}
          {status.is_approachable ? (
            <Button
              variant="contained"
              size="small"
              sx={{ mt: 1 }}
              disabled={laeuft}
              onClick={() => {
                setLaeuft(true);
                void expressInterest(
                  subjectId,
                  "Wir würden gern mit dir sprechen.",
                ).then(
                  (ergebnis: Awaited<ReturnType<typeof expressInterest>>) => {
                    setLaeuft(false);
                    setMeldung(
                      ergebnis.ok
                        ? "Interesse hinterlegt. Die Person entscheidet."
                        : ergebnis.error.detail,
                    );
                  },
                );
              }}
            >
              {laeuft ? "Wird hinterlegt…" : "Interesse zeigen"}
            </Button>
          ) : null}
        </>
      ) : null}
    </Box>
  );
}

/**
 * Belege aus GitHub, wenn die Person sie freigegeben hat.
 *
 * <strong>Die ersten fünf, nach letzter Änderung.</strong> Keine Auswahl nach
 * „Qualität" — die gibt es hier nicht, und eine Reihung wäre bereits eine
 * Wertung. Keine Repositories sind kein Mangel, sondern eine Auskunft.
 */
function GitHubBelege({ subjectId }: { subjectId: string }) {
  const verbindung = useAsync(
    (signal) => getGitHub(subjectId, signal),
    [subjectId],
  );
  const stand =
    verbindung.data?.ok === true ? verbindung.data.connection : null;

  if (stand === null) return null;

  return (
    <Box sx={{ mt: 2, pt: 2, borderTop: 1, borderColor: "divider" }}>
      <Link
        href={`https://github.com/${stand.login}`}
        target="_blank"
        rel="noreferrer noopener"
        variant="body2"
      >
        github.com/{stand.login}
      </Link>

      {stand.repositories.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          Keine öffentlichen Repositories.
        </Typography>
      ) : (
        <Box component="ul" sx={{ pl: 2.5, my: 1 }}>
          {stand.repositories.slice(0, 5).map((repo) => (
            <Typography component="li" variant="body2" key={repo.name}>
              <Link href={repo.url} target="_blank" rel="noreferrer noopener">
                {repo.name}
              </Link>
              {repo.language !== null ? ` · ${repo.language}` : null}
            </Typography>
          ))}
        </Box>
      )}

      {stand.fetched_at !== null ? (
        <Typography variant="caption" color="text.secondary">
          Stand: {new Date(stand.fetched_at).toLocaleDateString("de-DE")}
        </Typography>
      ) : null}
    </Box>
  );
}
