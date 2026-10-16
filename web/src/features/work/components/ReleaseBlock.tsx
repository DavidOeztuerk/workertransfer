import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { type MessageKey, failureKey } from "../../../shared/api/errors";
import { useAsync } from "../lib/useAsync";
import { useCompanyProfiles } from "../lib/useCompanyProfiles";
import { euro } from "../api/transfers";
import {
  RELEASE_CASES,
  RELEASE_RUNNING,
  type Release,
  type ReleaseMove,
  type ReleaseStatus,
  type ReleaseTerms,
  askRelease,
  getRelease,
  moveRelease,
} from "../api/releases";

export const RELEASE_STATUS_KEY: Record<ReleaseStatus, MessageKey> = {
  possible: "release.statusPossible",
  asked: "release.statusAsked",
  agreed: "release.statusAgreed",
  settled: "release.statusSettled",
  refused: "release.statusRefused",
  withdrawn: "release.statusWithdrawn",
  declined: "release.statusDeclined",
  closed: "release.statusClosed",
};

/**
 * The release negotiation inside one transfer card — for the person and for
 * the company she would move to (ADR-0050).
 *
 * <strong>It draws nothing when the server answers 404</strong>: no route, the
 * employer not asking, the consent taken back. Each of those would be a fact
 * about where she works, and the server refuses to say which; a grey "not
 * possible" here would say it anyway.
 *
 * <strong>She decides last.</strong> Two companies agreeing is shown to her as
 * a question, with "Ablehnen" beside "Zustimmen" — also because she may not
 * want to move at all.
 */
export function ReleaseBlock({ transferId }: { transferId: string }) {
  const { t } = useTranslation();
  const [error, setError] = useState<MessageKey | null>(null);
  const [busy, setBusy] = useState(false);
  const read = useAsync((signal) => getRelease(transferId, signal), [transferId]);
  const release = read.data?.ok ? read.data.release : null;
  const names = useCompanyProfiles(release ? [release.employer_id, release.hiring_id] : []);

  if (release === null) return null;

  const employer = names[release.employer_id]?.display_name ?? t("release.noProfile");

  async function step(run: () => Promise<{ ok: boolean; error?: Parameters<typeof failureKey>[0] }>) {
    setBusy(true);
    const result = await run();
    setBusy(false);
    setError(result.ok || result.error === undefined ? null : failureKey(result.error, "errors.stepFailed", RELEASE_CASES));
    read.reload();
  }

  const move = (name: ReleaseMove, terms?: ReleaseTerms) => () => void step(() => moveRelease(release.id!, name, terms));

  return (
    <Box
      component="section"
      aria-label={t("release.title")}
      sx={{ border: 1, borderColor: "divider", borderRadius: 2, p: 2, mb: 2, display: "grid", gap: 1.5 }}
    >
      <Typography variant="h3">{t("release.title")}</Typography>
      <Typography variant="body2" color="text.secondary">
        {t(RELEASE_STATUS_KEY[release.status])}
      </Typography>

      {release.side === "hiring" && release.status === "possible" ? (
        <>
          <Typography variant="body2">{t("release.possibleLead", { employer })}</Typography>
          <TermsForm
            submitLabel={t("release.ask")}
            busy={busy}
            onSubmit={(terms) => void step(() => askRelease(transferId, terms))}
          />
        </>
      ) : null}

      {release.side === "person" && release.status === "asked" ? (
        <Typography variant="body2">{t("release.personLead", { employer })}</Typography>
      ) : null}
      {release.side === "person" && release.status === "agreed" ? (
        <Alert severity="info">{t("release.agreedLead")}</Alert>
      ) : null}
      {release.side === "person" && release.status === "settled" ? (
        <Alert severity="success">
          {t("release.settledNote")}{" "}
          <Link component={RouterLink} to="/checklists">
            {t("footer.checklists")}
          </Link>
        </Alert>
      ) : null}

      {release.status !== "possible" ? <TermsView release={release} employer={employer} /> : null}

      {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}

      <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
        {release.side === "hiring" && release.status === "asked" && release.terms_by === "current" ? (
          <Button variant="contained" disabled={busy} onClick={move("accept")}>
            {t("release.accept")}
          </Button>
        ) : null}
        {release.side === "person" && release.status === "agreed" ? (
          <Button variant="contained" disabled={busy} onClick={move("agree")}>
            {t("release.agree")}
          </Button>
        ) : null}
        {release.side === "person" && RELEASE_RUNNING.includes(release.status) ? (
          <Button variant="text" disabled={busy} onClick={move("decline")}>
            {t("release.decline")}
          </Button>
        ) : null}
        {release.side === "hiring" && RELEASE_RUNNING.includes(release.status) ? (
          <Button variant="text" disabled={busy} onClick={move("withdraw")}>
            {t("release.withdraw")}
          </Button>
        ) : null}
      </Box>

      {release.side === "hiring" && release.status === "asked" ? (
        <TermsForm
          submitLabel={t("release.putTerms")}
          busy={busy}
          collapsed
          onSubmit={(terms) => void step(() => moveRelease(release.id!, "terms", terms))}
        />
      ) : null}
    </Box>
  );
}

/**
 * The terms on the table, and her regular earliest start beside them — the
 * figure both companies weigh the fee against, stated by her.
 */
export function TermsView({ release, employer, asker }: { release: Release; employer: string; asker?: string }) {
  const { t } = useTranslation();
  const by =
    release.terms_by === "current" ? employer : release.terms_by === "hiring" ? (asker ?? t("release.hiring")) : null;

  return (
    <Box
      component="dl"
      sx={{ display: "grid", gridTemplateColumns: "auto 1fr", columnGap: 2, rowGap: 0.5, m: 0 }}
    >
      <Typography component="dt" variant="body2" color="text.secondary">
        {t("release.employer")}
      </Typography>
      <Typography component="dd" sx={{ m: 0 }}>
        {employer}
      </Typography>
      <Typography component="dt" variant="body2" color="text.secondary">
        {t("release.month")}
      </Typography>
      <Typography component="dd" sx={{ m: 0 }}>
        {release.release_month ?? "—"}
      </Typography>
      <Typography component="dt" variant="body2" color="text.secondary">
        {t("release.fee")}
      </Typography>
      <Typography component="dd" sx={{ m: 0 }}>
        {release.fee_cents === null ? t("release.noFee") : euro(release.fee_cents)}
      </Typography>
      {by !== null ? (
        <>
          <Typography component="dt" variant="body2" color="text.secondary">
            {t("release.termsBy")}
          </Typography>
          <Typography component="dd" sx={{ m: 0 }}>
            {by}
          </Typography>
        </>
      ) : null}
      <Typography component="dt" variant="body2" color="text.secondary">
        {t("release.regularFrom")}
      </Typography>
      <Typography component="dd" sx={{ m: 0 }}>
        {release.regular_from === null ? t("release.regularUnknown") : release.regular_from}
        <Typography component="span" variant="caption" color="text.secondary" sx={{ display: "block" }}>
          {t("release.regularFromHint")}
        </Typography>
      </Typography>
    </Box>
  );
}

/**
 * A month and a fee, or none. Euros in the field, cents on the wire — and no
 * suggested figure anywhere: the platform names no price (ADR-0050).
 */
export function TermsForm({
  submitLabel,
  busy,
  collapsed = false,
  onSubmit,
}: {
  submitLabel: string;
  busy: boolean;
  collapsed?: boolean;
  onSubmit: (terms: ReleaseTerms) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(!collapsed);
  const [month, setMonth] = useState("");
  const [fee, setFee] = useState("");
  const [invalid, setInvalid] = useState(false);

  if (!open) {
    return (
      <Box>
        <Button variant="outlined" onClick={() => setOpen(true)}>
          {submitLabel}
        </Button>
      </Box>
    );
  }

  return (
    <Box
      component="form"
      onSubmit={(event) => {
        event.preventDefault();
        const trimmed = fee.trim().replace(",", ".");
        const euros = trimmed === "" ? null : Number(trimmed);
        if (euros !== null && !Number.isFinite(euros)) {
          setInvalid(true);
          return;
        }
        setInvalid(false);
        // Rounded so "1,005" does not arrive as a fraction of a cent.
        onSubmit({ release_month: month.trim(), fee_cents: euros === null ? null : Math.round(euros * 100) });
      }}
      sx={{ display: "grid", gap: 1.5, maxWidth: 420 }}
    >
      <TextField
        label={t("release.month")}
        helperText={t("release.monthHint")}
        type="month"
        required
        slotProps={{ inputLabel: { shrink: true } }}
        value={month}
        onChange={(event) => setMonth(event.target.value)}
      />
      <TextField
        label={t("release.feeField")}
        helperText={invalid ? t("release.feeInvalid") : t("release.feeHint")}
        error={invalid}
        inputMode="decimal"
        value={fee}
        onChange={(event) => setFee(event.target.value)}
      />
      <Typography variant="caption" color="text.secondary">
        {t("release.noPrice")}
      </Typography>
      <Box>
        <Button type="submit" variant="contained" disabled={busy || month.trim() === ""}>
          {submitLabel}
        </Button>
      </Box>
    </Box>
  );
}
