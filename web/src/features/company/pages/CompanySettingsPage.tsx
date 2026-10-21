import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type CompanyAi,
  type CompanyAiProvider,
  getCompanyAi,
  saveCompanyAi,
  saveCompanyAiKey,
} from "../api/companyAi";

/**
 * `/company/settings` — what an admin decides for the whole company.
 *
 * Today the company's own AI key (ADR-0051, Bauliste 6); the company's credit
 * and subscription join it here (ADR-0055).
 *
 * <strong>The header only hides, it does not protect.</strong> Every route
 * behind this page is `admin` at the server; a member reads who may instead
 * of a form that would answer 403 at the first click.
 */
export function CompanySettingsPage() {
  const { t } = useTranslation();
  const { forCompany, tenantId } = useActor();

  if (!forCompany || tenantId === null) {
    return (
      <PageShell title={t("companySettings.title")} narrow>
        <Card>
          <CardContent>
            <Typography>{t("companySettings.companyOnly")}</Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell title={t("companySettings.title")} narrow lead={t("companySettings.lead")}>
      <CompanyAiBlock tenantId={tenantId} />
    </PageShell>
  );
}

/**
 * Who writes the company's drafts — and the key, which only goes in.
 *
 * <strong>No address field</strong>, as on the person's side: the address
 * belongs to the provider and is set by the platform, so a typed one cannot
 * send the team's drafts to any server.
 */
function CompanyAiBlock({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation();
  const current = useAsync((signal) => getCompanyAi(tenantId, signal), [tenantId]);
  const [ai, setAi] = useState<CompanyAi | null>(null);
  const [aiKey, setAiKey] = useState("");
  const [saved, setSaved] = useState(false);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (current.data?.ok) setAi(current.data.ai);
  }, [current.data]);

  const notAdmin = current.data !== null && !current.data.ok && current.data.reason === "not-admin";

  async function apply(part: Partial<CompanyAi>) {
    if (ai === null) return;
    const next = { ...ai, ...part };
    const result = await saveCompanyAi(tenantId, { provider: next.provider, model: next.model });
    setFailed(!result.ok);
    setSaved(result.ok);
    if (result.ok) setAi(result.ai);
  }

  async function storeKey() {
    const ok = await saveCompanyAiKey(tenantId, aiKey);
    setFailed(!ok);
    setSaved(ok);
    if (ok) {
      setAiKey("");
      current.reload();
    }
  }

  return (
    <Card>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("companySettings.aiTitle")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("companySettings.aiLead")}
        </Typography>

        {current.pending && ai === null ? <LoadingBlock label={t("settings.loading")} /> : null}

        {notAdmin ? (
          <Alert severity="info" role="status">
            {t("companySettings.adminOnly")}
          </Alert>
        ) : null}

        {!notAdmin && current.data !== null && !current.data.ok ? (
          <Alert severity="error">{t("settings.unavailable")}</Alert>
        ) : null}

        {ai !== null ? (
          <Box sx={{ display: "grid", gap: 2.5 }}>
            <TextField
              select
              label={t("settings.provider")}
              value={ai.provider}
              helperText={t("companySettings.providerHint")}
              onChange={(event) => void apply({ provider: event.target.value as CompanyAiProvider })}
            >
              <MenuItem value="none">{t("settings.providerNone")}</MenuItem>
              <MenuItem value="anthropic">{t("settings.providerAnthropic")}</MenuItem>
              <MenuItem value="openai">{t("settings.providerOpenAi")}</MenuItem>
            </TextField>

            {ai.provider !== "none" ? (
              <>
                <TextField
                  label={t("settings.model")}
                  helperText={t(ai.provider === "anthropic" ? "settings.modelHintAnthropic" : "settings.modelHintOpenAi")}
                  value={ai.model}
                  onChange={(event) => setAi({ ...ai, model: event.target.value })}
                  onBlur={() => void apply({ model: ai.model })}
                />

                <Box>
                  <TextField
                    fullWidth
                    label={t("settings.key")}
                    type="password"
                    autoComplete="off"
                    value={aiKey}
                    onChange={(event) => setAiKey(event.target.value)}
                    helperText={t("companySettings.keyHint")}
                  />
                  <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1.5, mt: 1.5, alignItems: "center" }}>
                    <Button variant="outlined" onClick={() => void storeKey()}>
                      {t("settings.keySave")}
                    </Button>
                    <Typography variant="body2" color="text.secondary">
                      {ai.keyPresent ? t("settings.keyStored", { endung: ai.keyTail }) : t("settings.keyNone")}
                    </Typography>
                  </Box>
                </Box>
              </>
            ) : null}

            {saved ? (
              <Alert severity="success" role="status">
                {t("settings.savedShort")}
              </Alert>
            ) : null}
            {failed ? <Alert severity="error">{t("errors.settingsNotSaved")}</Alert> : null}
          </Box>
        ) : null}
      </CardContent>
    </Card>
  );
}
