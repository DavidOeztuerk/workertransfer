import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

import { ConsentSwitch } from "../../../shared/components/ui";
import { type MessageKey, failureKey } from "../../../shared/api/errors";
import { useAsync } from "../../work/lib/useAsync";
import { useCompanyProfiles } from "../../work/lib/useCompanyProfiles";
import { RELEASE_STATUS_KEY, TermsForm, TermsView } from "../../work/components/ReleaseBlock";
import {
  RELEASE_CASES,
  RELEASE_RUNNING,
  type Release,
  type ReleaseMove,
  type ReleaseTerms,
  getEmployerSetting,
  listInquiries,
  moveRelease,
  setEmployerSetting,
} from "../../work/api/releases";

/**
 * Inquiries from other companies about the own staff (C1, ADR-0050) — the
 * current employer's side.
 *
 * <strong>The switch is the employer's half of two consents.</strong> It
 * carries no word like "insolvency" and is visible nowhere outside the
 * company. Without the person's own half it does nothing at all.
 *
 * <strong>A person appears here only when a company actually asks</strong> —
 * never a list of who opened a route. Which of the own staff are listening
 * is the one thing this page must not be able to show.
 */
export function EmployerInquiries() {
  const { t } = useTranslation();
  const [error, setError] = useState<MessageKey | null>(null);
  const [busy, setBusy] = useState(false);
  const setting = useAsync((signal) => getEmployerSetting(signal), []);
  const inquiries = useAsync((signal) => listInquiries(signal), []);
  const list = inquiries.data?.ok ? inquiries.data.inquiries : [];
  const names = useCompanyProfiles(list.map((item) => item.hiring_id));

  async function toggle(next: boolean) {
    setBusy(true);
    const result = await setEmployerSetting(next);
    setBusy(false);
    setError(result.ok ? null : failureKey(result.error, "errors.stepFailed", RELEASE_CASES));
    setting.reload();
  }

  async function step(id: string, move: ReleaseMove, terms?: ReleaseTerms) {
    setBusy(true);
    const result = await moveRelease(id, move, terms);
    setBusy(false);
    setError(result.ok ? null : failureKey(result.error, "errors.stepFailed", RELEASE_CASES));
    inquiries.reload();
  }

  return (
    <Card component="section" aria-label={t("employerInquiries.title")} sx={{ mb: 3 }}>
      <CardContent sx={{ display: "grid", gap: 2 }}>
        <Typography variant="h2">{t("employerInquiries.title")}</Typography>

        {setting.data?.ok ? (
          <ConsentSwitch
            label={t("employerInquiries.settingLabel")}
            hint={t("employerInquiries.settingHint")}
            checked={setting.data.accepts}
            disabled={busy}
            onChange={(next) => void toggle(next)}
          />
        ) : null}

        {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}

        {inquiries.data?.ok && list.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {t("employerInquiries.empty")}
          </Typography>
        ) : null}

        <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gap: 2 }}>
          {list.map((inquiry) => (
            <Inquiry
              key={inquiry.id}
              inquiry={inquiry}
              asker={names[inquiry.hiring_id]?.display_name ?? t("release.noProfile")}
              busy={busy}
              onStep={(move, terms) => void step(inquiry.id!, move, terms)}
            />
          ))}
        </Box>
      </CardContent>
    </Card>
  );
}

function Inquiry({
  inquiry,
  asker,
  busy,
  onStep,
}: {
  inquiry: Release;
  asker: string;
  busy: boolean;
  onStep: (move: ReleaseMove, terms?: ReleaseTerms) => void;
}) {
  const { t } = useTranslation();
  const running = RELEASE_RUNNING.includes(inquiry.status);

  return (
    <Box component="li" sx={{ border: 1, borderColor: "divider", borderRadius: 2, p: 2, display: "grid", gap: 1.5 }}>
      <Typography variant="h3">{t("employerInquiries.about", { name: inquiry.person_name ?? "—" })}</Typography>
      <Typography variant="body2" color="text.secondary">
        {t("employerInquiries.from", { company: asker })} · {t(RELEASE_STATUS_KEY[inquiry.status])}
      </Typography>

      <TermsView release={inquiry} employer={t("employerInquiries.you")} asker={asker} />

      <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
        {inquiry.status === "asked" && inquiry.terms_by === "hiring" ? (
          <Button variant="contained" disabled={busy} onClick={() => onStep("accept")}>
            {t("release.accept")}
          </Button>
        ) : null}
        {running ? (
          <Button variant="text" disabled={busy} onClick={() => onStep("refuse")}>
            {t("release.refuse")}
          </Button>
        ) : null}
      </Box>

      {inquiry.status === "asked" ? (
        <TermsForm
          submitLabel={t("release.putTerms")}
          busy={busy}
          collapsed
          onSubmit={(terms) => onStep("terms", terms)}
        />
      ) : null}
    </Box>
  );
}
