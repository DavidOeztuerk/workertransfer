import { useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
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

/** Der Stand als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const TITEL: Record<Transfer["status"], string> = {
  interested: "transfers.statusInterested",
  talking: "transfers.statusTalking",
  offered: "transfers.statusOffered",
  accepted: "transfers.statusAccepted",
  completed: "transfers.statusCompleted",
  declined: "transfers.statusDeclined",
  withdrawn: "transfers.statusWithdrawn",
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
  const { t } = useTranslation();
  const { signedIn, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const gespraeche = useAsync(
    (signal) => listMyTransfers(signal),
    [subjectId],
    signedIn,
  );

  if (!signedIn) {
    return (
      <PageShell title={t("transfers.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              <Trans
                i18nKey="transfers.signInRequired"
                components={{ 1: <Link component={RouterLink} to="/login" /> }}
              />
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function ziehen(id: string, move: PersonAction) {
    setLaeuft(true);
    const result = await personMove(id, move);
    setLaeuft(false);
    if (!result.ok) setFehler(result.error.detail);
    else setFehler(null);
    gespraeche.reload();
  }

  const list = gespraeche.data?.ok ? gespraeche.data.transfers : [];

  return (
    <PageShell title={t("transfers.title")} narrow lead="">
      <Typography color="text.secondary" sx={{ mb: 3, maxWidth: "62ch" }}>
        <Trans
          i18nKey="transfers.intro"
          components={{ 1: <Link component={RouterLink} to="/market" /> }}
        />
      </Typography>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      {gespraeche.pending ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("transfers.loading")} />
          </CardContent>
        </Card>
      ) : null}

      {gespraeche.data !== null && !gespraeche.data.ok ? (
        <Alert severity="error">{gespraeche.data.error.detail}</Alert>
      ) : null}

      {gespraeche.data?.ok && list.length === 0 ? (
        <EmptyBlock title={t("transfers.empty")} />
      ) : null}

      <Box
        component="ul"
        sx={{
          display: "flex",
          flexDirection: "column",
          gap: 2,
          listStyle: "none",
          p: 0,
          m: 0,
        }}
      >
        {list.map((gespraech: Transfer) => (
          <Gespraechskarte
            key={gespraech.id}
            gespraech={gespraech}
            locked={running}
            onZug={(move) => void ziehen(gespraech.id, move)}
          />
        ))}
      </Box>
    </PageShell>
  );
}

function Gespraechskarte({
  gespraech,
  locked,
  onZug,
}: {
  gespraech: Transfer;
  locked: boolean;
  onZug: (move: PersonAction) => void;
}) {
  const { t } = useTranslation();
  const laeuftNoch = RUNNING.includes(gespraech.status);
  const brauchtFreigabe =
    gespraech.requires_release && !gespraech.release_confirmed;
  const zeigtAngebot =
    gespraech.status === "offered" || gespraech.status === "accepted";

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t(TITEL[gespraech.status])}
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
              {t("transfers.offer")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_note === "" ? "—" : gespraech.offer_note}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("transfers.start")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {gespraech.offer_start_on ?? "—"}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("transfers.fee")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {euro(gespraech.offer_fee_cents)}
            </Typography>
          </Box>
        ) : null}

        {gespraech.status === "accepted" && brauchtFreigabe ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            <Trans
              i18nKey="transfers.releaseMissing"
              components={{ 1: <strong /> }}
            />
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {gespraech.status === "interested" ? (
            <Button
              variant="contained"
              onClick={() => onZug("accept-talk")}
              disabled={locked}
            >
              {t("transfers.acceptConversation")}
            </Button>
          ) : null}
          {gespraech.status === "offered" ? (
            <Button
              variant="contained"
              onClick={() => onZug("accept-offer")}
              disabled={locked}
            >
              {t("transfers.acceptOffer")}
            </Button>
          ) : null}
          {gespraech.status === "accepted" && brauchtFreigabe ? (
            <Button
              variant="contained"
              onClick={() => onZug("confirm-release")}
              disabled={locked}
            >
              {t("transfers.confirmRelease")}
            </Button>
          ) : null}
          {laeuftNoch ? (
            <Button
              variant="text"
              onClick={() => onZug("decline")}
              disabled={locked}
            >
              {t("common.decline")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
