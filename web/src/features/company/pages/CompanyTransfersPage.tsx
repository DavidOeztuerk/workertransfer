import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type CompanyAction,
  RUNNING,
  type Transfer,
  companyMove,
  euro,
  listCompanyTransfers,
  makeOffer,
} from "../../work/api/transfers";

/** The state as a catalogue key — the wording lies in the catalogues. */
const STATUS_TITLES: Record<Transfer["status"], string> = {
  interested: "companyTransfers.statusInterested",
  talking: "companyTransfers.statusTalking",
  offered: "companyTransfers.statusOffered",
  accepted: "companyTransfers.statusAccepted",
  completed: "companyTransfers.statusCompleted",
  declined: "companyTransfers.statusDeclined",
  withdrawn: "companyTransfers.statusWithdrawn",
};

/**
 * <c>/company/transfers</c> — the conversations from the company's side.
 *
 * <strong>The placement fee is recorded, not moved.</strong> This platform
 * handles no money. The figure stands there so both sides have the same one in
 * view — not because anything would be transferred here.
 *
 * <strong>The company may only complete when no release is
 * outstanding.</strong> If the person must first be let go by their employer,
 * the last step is theirs; the company cannot take it for them.
 */
export function CompanyTransfersPage() {
  const { t } = useTranslation();
  const { forCompany, tenantId } = useActor();
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const transfers = useAsync(
    (signal) => listCompanyTransfers(signal),
    [tenantId],
    forCompany,
  );

  if (!forCompany) {
    return (
      <PageShell title={t("companyTransfers.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("companyTransfers.companyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function move(id: string, action: CompanyAction) {
    setRunning(true);
    const result = await companyMove(id, action);
    setRunning(false);
    setError(result.ok ? null : result.error.detail);
    transfers.reload();
  }

  async function offer(
    id: string,
    text: string,
    start: string,
    fee: string,
  ) {
    setRunning(true);
    const result = await makeOffer(id, {
      note: text,
      start_on: start.trim() === "" ? null : start.trim(),
      // Euros in the field, cents on the wire — rounded so "1,005" does not
      // arrive as a fraction of a cent.
      fee_cents:
        fee.trim() === "" ? null : Math.round(Number(fee) * 100),
    });
    setRunning(false);
    setError(result.ok ? null : result.error.detail);
    transfers.reload();
  }

  const result = transfers.data;
  const list = result?.ok ? result.transfers : [];

  return (
    <PageShell
      title={t("companyTransfers.title")}
      narrow
      lead={t("companyTransfers.lead")}
    >
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      {transfers.pending ? (
        <Card>
          <CardContent>
            <LoadingBlock label={t("companyTransfers.loading")} />
          </CardContent>
        </Card>
      ) : null}

      {result !== null && !result.ok ? (
        <Alert severity="error">{result.error.detail}</Alert>
      ) : null}

      {result?.ok && list.length === 0 ? (
        <EmptyBlock title={t("companyTransfers.empty")} />
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
            onOffer={(text, start, fee) =>
              void offer(transfer.id, text, start, fee)
            }
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
  onOffer,
}: {
  transfer: Transfer;
  locked: boolean;
  onMove: (action: CompanyAction) => void;
  onOffer: (text: string, start: string, fee: string) => void;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState("");
  const [start, setStart] = useState("");
  const [fee, setFee] = useState("");

  const stillRunning = RUNNING.includes(transfer.status);
  // The rule is SAID, not only enforced: silently dropping the completion
  // button looks like a defect from outside, and whoever does not know the rule
  // hunts for it in the wrong place. The sentence beside it says two things that
  // both belong to the company — that the last step is not theirs, and that the
  // platform does NOT ask the current employer. The second is the promise this
  // market hangs on for the person.
  const mayComplete =
    transfer.status === "accepted" && !transfer.requires_release;
  const showsOffer =
    transfer.status === "offered" || transfer.status === "accepted";

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t(STATUS_TITLES[transfer.status])}
        </Typography>

        {transfer.requires_release && !transfer.release_confirmed ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            <strong>{t("transfer.needsReleaseTitle")}</strong>{" "}
            {t("transfer.needsReleaseText")}{" "}
            <strong>{t("transfer.needsReleasePromise")}</strong>
          </Alert>
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
              {t("companyTransfers.offer")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {transfer.offer_note === "" ? "—" : transfer.offer_note}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("companyTransfers.start")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {transfer.offer_start_on ?? "—"}
            </Typography>
            <Typography component="dt" variant="body2" color="text.secondary">
              {t("companyTransfers.fee")}
            </Typography>
            <Typography component="dd" sx={{ m: 0 }}>
              {euro(transfer.offer_fee_cents)}
            </Typography>
          </Box>
        ) : null}

        {transfer.status === "talking" ? (
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              onOffer(text, start, fee);
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2, mb: 2 }}
          >
            <TextField
              label={t("companyTransfers.offer")}
              helperText={t("companyTransfers.offerHint")}
              value={text}
              onChange={(e) => setText(e.target.value)}
              slotProps={{ htmlInput: { maxLength: 2000 } }}
              multiline
              minRows={3}
            />
            <TextField
              label={t("companyTransfers.start")}
              helperText={t("companyTransfers.startHint")}
              placeholder="2026-11"
              value={start}
              onChange={(e) => setStart(e.target.value)}
            />
            <TextField
              label={t("companyTransfers.fee")}
              helperText={t("companyTransfers.feeHint")}
              value={fee}
              onChange={(e) => setFee(e.target.value)}
            />
            <Box>
              <Button type="submit" variant="contained" disabled={locked}>
                {t("companyTransfers.makeOffer")}
              </Button>
            </Box>
          </Box>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {mayComplete ? (
            <Button
              variant="contained"
              onClick={() => onMove("complete")}
              disabled={locked}
            >
              {t("companyTransfers.complete")}
            </Button>
          ) : null}
          {stillRunning ? (
            <Button
              variant="text"
              onClick={() => onMove("withdraw")}
              disabled={locked}
            >
              {t("common.withdraw")}
            </Button>
          ) : null}
        </Box>
      </CardContent>
    </Card>
  );
}
