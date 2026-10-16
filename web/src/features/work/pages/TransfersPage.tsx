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
import { failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { ReleaseBlock } from "../components/ReleaseBlock";
import { useAsync } from "../lib/useAsync";
import {
  type PersonAction,
  RUNNING,
  type Transfer,
  listMyTransfers,
  personMove,
} from "../api/transfers";

/** The status as a catalogue key — the wording lies in the catalogues. */
const STATUS_KEY: Record<Transfer["status"], string> = {
  interested: "transfers.statusInterested",
  talking: "transfers.statusTalking",
  offered: "transfers.statusOffered",
  accepted: "transfers.statusAccepted",
  completed: "transfers.statusCompleted",
  declined: "transfers.statusDeclined",
  withdrawn: "transfers.statusWithdrawn",
};

/**
 * <c>/transfers</c> — one's own conversations about a switch.
 *
 * <strong>A company can only approach when the market status is
 * released</strong> and the person approachable right now. Declining is
 * possible at any time, in every step — that is no courtesy but the condition
 * for a conversation being reasonable at all.
 *
 * <strong>The last step belongs to the person.</strong> This platform asks the
 * current employer only over the route she and her employer both opened
 * (ADR-0050), and even then she agrees last. Whether she may leave, she
 * confirms herself.
 */
export function TransfersPage() {
  const { t } = useTranslation();
  const { signedIn, subjectId } = useActor();
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const transfers = useAsync(
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

  async function move(id: string, action: PersonAction) {
    setRunning(true);
    const result = await personMove(id, action);
    setRunning(false);
    // A 409 means the case moved on meanwhile (the company withdrew, or a
    // step was already taken) — a catalogue text, never the server's sentence.
    if (!result.ok) {
      setError(
        t(
          failureKey(result.error, "errors.stepFailed", [
            { status: 409, key: "transfers.stepNotNow" },
          ]),
        ),
      );
    } else setError(null);
    transfers.reload();
  }

  const list = transfers.data?.ok ? transfers.data.transfers : [];

  return (
    <PageShell title={t("transfers.title")} narrow lead="">
      <Typography color="text.secondary" sx={{ mb: 3, maxWidth: "62ch" }}>
        <Trans
          i18nKey="transfers.intro"
          components={{ 1: <Link component={RouterLink} to="/market" /> }}
        />
      </Typography>

      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      {transfers.pending ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("transfers.loading")} />
          </CardContent>
        </Card>
      ) : null}

      {transfers.data !== null && !transfers.data.ok ? (
        <Alert severity="error">
          {t(failureKey(transfers.data.error, "errors.listNotLoaded"))}
        </Alert>
      ) : null}

      {transfers.data?.ok && list.length === 0 ? (
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
        {list.map((transfer: Transfer) => (
          <TransferCard
            key={transfer.id}
            transfer={transfer}
            locked={running}
            onMove={(action) => void move(transfer.id, action)}
          />
        ))}
      </Box>
    </PageShell>
  );
}

function TransferCard({
  transfer,
  locked,
  onMove,
}: {
  transfer: Transfer;
  locked: boolean;
  onMove: (action: PersonAction) => void;
}) {
  const { t } = useTranslation();
  const stillRunning = RUNNING.includes(transfer.status);
  const needsRelease = transfer.requires_release && !transfer.release_confirmed;
  const showsOffer = transfer.status === "offered" || transfer.status === "accepted";

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t(STATUS_KEY[transfer.status])}
        </Typography>

        {transfer.message !== "" ? (
          <Typography sx={{ mb: 2 }}>{transfer.message}</Typography>
        ) : null}

        {showsOffer ? (
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
              {transfer.offer_note === "" ? "—" : transfer.offer_note}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("transfers.start")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {transfer.offer_start_on ?? "—"}
              {transfer.starts_before_earliest === true ? (
                <Typography component="span" variant="body2" color="warning.main" sx={{ display: "block" }}>
                  {t("transfers.startsBeforeEarliest")}{" "}
                  <Link component={RouterLink} to="/checklists">
                    {t("footer.checklists")}
                  </Link>
                </Typography>
              ) : null}
            </Typography>
          </Box>
        ) : null}

        {transfer.requires_release ? <ReleaseBlock transferId={transfer.id} /> : null}

        {transfer.status === "accepted" && needsRelease ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            <Trans
              i18nKey="transfers.releaseMissing"
              components={{ 1: <strong /> }}
            />
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {transfer.status === "interested" ? (
            <Button
              variant="contained"
              onClick={() => onMove("accept-talk")}
              disabled={locked}
            >
              {t("transfers.acceptConversation")}
            </Button>
          ) : null}
          {transfer.status === "offered" ? (
            <Button
              variant="contained"
              onClick={() => onMove("accept-offer")}
              disabled={locked}
            >
              {t("transfers.acceptOffer")}
            </Button>
          ) : null}
          {transfer.status === "accepted" && needsRelease ? (
            <Button
              variant="contained"
              onClick={() => onMove("confirm-release")}
              disabled={locked}
            >
              {t("transfers.confirmRelease")}
            </Button>
          ) : null}
          {stillRunning ? (
            <Button
              variant="text"
              onClick={() => onMove("decline")}
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
