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
import { useHandelnder } from "../lib/session";
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

/** Der Stand als Katalogschlüssel — der Wortlaut liegt in den Katalogen. */
const TITEL: Record<Transfer["status"], string> = {
  interested: "companyTransfers.statusInterested",
  talking: "companyTransfers.statusTalking",
  offered: "companyTransfers.statusOffered",
  accepted: "companyTransfers.statusAccepted",
  completed: "companyTransfers.statusCompleted",
  declined: "companyTransfers.statusDeclined",
  withdrawn: "companyTransfers.statusWithdrawn",
};

/**
 * <c>/company/transfers</c> — die Gespräche aus Sicht des Unternehmens.
 *
 * <strong>Die Ablöse wird festgehalten, nicht bewegt.</strong> Diese Plattform
 * führt kein Geld. Die Zahl steht da, damit beide Seiten dieselbe im Blick
 * haben — nicht, weil hier etwas überwiesen würde.
 *
 * <strong>Abschliessen darf das Unternehmen nur, wenn keine Freigabe
 * aussteht.</strong> Muss die Person erst von ihrem Arbeitgeber gelassen
 * werden, gehört der letzte Schritt ihr; das Unternehmen kann ihn nicht für sie
 * tun.
 */
export function CompanyTransfersPage() {
  const { t } = useTranslation();
  const { fuerFirma, tenantId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [running, setLaeuft] = useState(false);

  const transfers = useAsync(
    (signal) => listCompanyTransfers(signal),
    [tenantId],
    fuerFirma,
  );

  if (!fuerFirma) {
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

  async function ziehen(id: string, move: CompanyAction) {
    setLaeuft(true);
    const result = await companyMove(id, move);
    setLaeuft(false);
    setFehler(result.ok ? null : result.error.detail);
    transfers.reload();
  }

  async function anbieten(
    id: string,
    text: string,
    start: string,
    abloese: string,
  ) {
    setLaeuft(true);
    const result = await makeOffer(id, {
      note: text,
      start_on: start.trim() === "" ? null : start.trim(),
      // Euro im Feld, Cent auf dem Draht — gerundet, damit "1,005" nicht als
      // Bruchteil eines Cents ankommt.
      fee_cents:
        abloese.trim() === "" ? null : Math.round(Number(abloese) * 100),
    });
    setLaeuft(false);
    setFehler(result.ok ? null : result.error.detail);
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
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
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
          <Transferkarte
            key={transfer.id}
            transfer={transfer}
            locked={running}
            onZug={(move) => void ziehen(transfer.id, move)}
            onAngebot={(text, start, abloese) =>
              void anbieten(transfer.id, text, start, abloese)
            }
          />
        ))}
      </Box>
    </PageShell>
  );
}

function Transferkarte({
  transfer,
  locked,
  onZug,
  onAngebot,
}: {
  transfer: Transfer;
  locked: boolean;
  onZug: (move: CompanyAction) => void;
  onAngebot: (text: string, start: string, abloese: string) => void;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState("");
  const [start, setStart] = useState("");
  const [abloese, setAbloese] = useState("");

  const laeuftNoch = RUNNING.includes(transfer.status);
  // The rule is SAID, not only enforced: silently dropping the completion
  // button looks like a defect from outside, and whoever does not know the rule
  // hunts for it in the wrong place. The sentence beside it says two things that
  // both belong to the company — that the last step is not theirs, and that the
  // platform does NOT ask the current employer. The second is the promise this
  // market hangs on for the person.
  const darfAbschliessen =
    transfer.status === "accepted" && !transfer.requires_release;
  const zeigtAngebot =
    transfer.status === "offered" || transfer.status === "accepted";

  return (
    <Card component="li">
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t(TITEL[transfer.status])}
        </Typography>

        {transfer.requires_release && !transfer.release_confirmed ? (
          <Alert severity="info" sx={{ mb: 2 }}>
            <strong>{t("transfer.needsReleaseTitle")}</strong>{" "}
            {t("transfer.needsReleaseText")}{" "}
            <strong>{t("transfer.needsReleasePromise")}</strong>
          </Alert>
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
              onAngebot(text, start, abloese);
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
              value={abloese}
              onChange={(e) => setAbloese(e.target.value)}
            />
            <Box>
              <Button type="submit" variant="contained" disabled={locked}>
                {t("companyTransfers.makeOffer")}
              </Button>
            </Box>
          </Box>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          {darfAbschliessen ? (
            <Button
              variant="contained"
              onClick={() => onZug("complete")}
              disabled={locked}
            >
              {t("companyTransfers.complete")}
            </Button>
          ) : null}
          {laeuftNoch ? (
            <Button
              variant="text"
              onClick={() => onZug("withdraw")}
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
