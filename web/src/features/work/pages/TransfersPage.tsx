import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type PersonAction,
  RUNNING,
  type Transfer,
  euro,
  listMyTransfers,
  personMove,
} from "../api/transfers";

const TITEL: Record<Transfer["status"], string> = {
  interested: "Ein Unternehmen hat Interesse",
  talking: "Ihr seid im Gespräch",
  offered: "Es liegt ein Angebot vor",
  accepted: "Du hast angenommen",
  completed: "Abgeschlossen",
  declined: "Von dir abgelehnt",
  withdrawn: "Vom Unternehmen zurückgezogen",
};

/**
 * <c>/transfers</c> — die eigenen Gespräche über einen Wechsel.
 *
 * <strong>Ein Unternehmen kann nur zugehen, wenn der Marktstatus freigegeben
 * ist</strong> und die Person gerade ansprechbar. Ablehnen geht jederzeit, in
 * jedem Schritt — das ist keine Höflichkeit, sondern die Bedingung dafür, dass
 * ein Gespräch überhaupt zumutbar ist.
 *
 * <strong>Der letzte Schritt gehört der Person.</strong> Diese Plattform fragt
 * den jetzigen Arbeitgeber nicht: sie weiss nicht, wer er ist, und soll es nicht
 * wissen. Ob jemand gehen darf, bestätigt er selbst.
 */
export function TransfersPage() {
  const { angemeldet, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const gespraeche = useAsync((signal) => listMyTransfers(signal), [subjectId], angemeldet);

  if (!angemeldet) {
    return (
      <PageShell title="Meine Gespräche" narrow>
        <Card>
          <CardContent>
            <Typography>
              Bitte{" "}
              <Button component={RouterLink} to="/login" variant="text" size="small">
                anmelden
              </Button>
              , um deine Gespräche zu sehen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function ziehen(id: string, zug: PersonAction) {
    setLaeuft(true);
    const ergebnis = await personMove(id, zug);
    setLaeuft(false);
    if (!ergebnis.ok) setFehler(ergebnis.error.detail);
    else setFehler(null);
    gespraeche.reload();
  }

  const liste = gespraeche.data?.ok ? gespraeche.data.transfers : [];

  return (
    <PageShell
      title="Meine Gespräche"
      narrow
      lead=""
    >
      <Typography color="text.secondary" sx={{ mb: 3, maxWidth: "62ch" }}>
        Ein Unternehmen kann nur zugehen, wenn du ihm deinen{" "}
        <Link component={RouterLink} to="/market">
          Marktstatus freigegeben
        </Link>{" "}
        hast und gerade ansprechbar bist. Ablehnen kannst du jederzeit, in jedem Schritt.
      </Typography>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      {gespraeche.pending ? (
        <Card>
          <CardContent>
            <LoadingBlock label="Gespräche werden geladen…" />
          </CardContent>
        </Card>
      ) : null}

      {gespraeche.data !== null && !gespraeche.data.ok ? (
        <Alert severity="error">{gespraeche.data.error.detail}</Alert>
      ) : null}

      {gespraeche.data?.ok && liste.length === 0 ? (
        <EmptyBlock title="Es läuft gerade kein Gespräch." />
      ) : null}

      <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
        {liste.map((gespraech: Transfer) => (
          <Gespraechskarte
            key={gespraech.id}
            gespraech={gespraech}
            gesperrt={laeuft}
            onZug={(zug) => void ziehen(gespraech.id, zug)}
          />
        ))}
      </Box>
    </PageShell>
  );
}

function Gespraechskarte({
  gespraech,
  gesperrt,
  onZug,
}: {
  gespraech: Transfer;
  gesperrt: boolean;
  onZug: (zug: PersonAction) => void;
}) {
  const laeuftNoch = RUNNING.includes(gespraech.status);
  const brauchtFreigabe = gespraech.requires_release && !gespraech.release_confirmed;
  const zeigtAngebot = gespraech.status === "offered" || gespraech.status === "accepted";

  return (
    <Card>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {TITEL[gespraech.status]}
        </Typography>

        {gespraech.message !== "" ? (
          <Typography sx={{ mb: 2 }}>{gespraech.message}</Typography>
        ) : null}

        {zeigtAngebot ? (
          <Box
            component="dl"
            sx={{
              display: "grid",
              gridTemplateColumns: "auto 1fr",
              columnGap: 2,
              rowGap: 0.5,
              mb: 2,
            }}
          >
            <Typography component="dt" variant="body2" color="text.secondary">
              Angebot
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_note === "" ? "—" : gespraech.offer_note}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              Start
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_start_on ?? "—"}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              Ablöse
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {euro(gespraech.offer_fee_cents)}
            </Typography>
          </Box>
        ) : null}

        {gespraech.status === "accepted" && brauchtFreigabe ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            Es fehlt noch, dass dein Arbeitgeber dich gehen lässt. Diese Plattform fragt ihn nicht
            — sie weiß nicht, wer er ist und soll es nicht wissen. Bestätige selbst, sobald es
            geklärt ist: <strong>damit ist der Transfer abgeschlossen.</strong> Der letzte Schritt
            gehört dir, weil nur du weißt, ob du gehen darfst.
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {gespraech.status === "interested" ? (
            <Button variant="contained" onClick={() => onZug("accept-talk")} disabled={gesperrt}>
              Gespräch annehmen
            </Button>
          ) : null}
          {gespraech.status === "offered" ? (
            <Button variant="contained" onClick={() => onZug("accept-offer")} disabled={gesperrt}>
              Angebot annehmen
            </Button>
          ) : null}
          {gespraech.status === "accepted" && brauchtFreigabe ? (
            <Button
              variant="contained"
              onClick={() => onZug("confirm-release")}
              disabled={gesperrt}
            >
              Freigabe bestätigen und abschließen
            </Button>
          ) : null}
          {laeuftNoch ? (
            <Button variant="text" onClick={() => onZug("decline")} disabled={gesperrt}>
              Ablehnen
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
