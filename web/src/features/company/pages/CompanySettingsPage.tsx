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
 * <strong>An address field only for the company's own server</strong>, and
 * the server binds it: https, under the company's proven domain. For a cloud
 * provider the address belongs to the provider and is set by the platform.
 * The own server is saved with its own button, once address and model are
 * there — choosing it alone changes nothing at the server.
 */
function CompanyAiBlock({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation();
  const current = useAsync((signal) => getCompanyAi(tenantId, signal), [tenantId]);
  const [ai, setAi] = useState<CompanyAi | null>(null);
  const [aiKey, setAiKey] = useState("");
  const [saved, setSaved] = useState(false);
  const [failed, setFailed] = useState(false);
  const [refused, setRefused] = useState(false);

  useEffect(() => {
    if (current.data?.ok) setAi(current.data.ai);
  }, [current.data]);

  const notAdmin = current.data !== null && !current.data.ok && current.data.reason === "not-admin";

  async function apply(part: Partial<CompanyAi>) {
    if (ai === null) return;
    const next = { ...ai, ...part };
    const result = await saveCompanyAi(tenantId, {
      provider: next.provider,
      model: next.model,
      address: next.address,
    });
    setRefused(!result.ok && result.reason === "invalid");
    setFailed(!result.ok && result.reason !== "invalid");
    setSaved(result.ok);
    if (result.ok) setAi(result.ai);
  }

  function choose(provider: CompanyAiProvider) {
    if (ai === null) return;
    if (provider === "own") {
      // Nothing is saved yet: an own server without address and model would
      // only be refused. The button below saves it.
      setAi({
        ...ai,
        provider,
        model: ai.provider === "own" ? ai.model : "",
        address: ai.provider === "own" ? ai.address : "",
      });
      setSaved(false);
      return;
    }
    void apply({ provider });
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
              onChange={(event) => choose(event.target.value as CompanyAiProvider)}
            >
              <MenuItem value="none">{t("settings.providerNone")}</MenuItem>
              <MenuItem value="anthropic">{t("settings.providerAnthropic")}</MenuItem>
              <MenuItem value="openai">{t("settings.providerOpenAi")}</MenuItem>
              <MenuItem value="own">{t("companySettings.providerOwn")}</MenuItem>
            </TextField>

            {ai.provider === "own" ? (
              <>
                <TextField
                  fullWidth
                  label={t("companySettings.address")}
                  helperText={t("companySettings.addressHint")}
                  value={ai.address}
                  slotProps={{ htmlInput: { inputMode: "url" } }}
                  onChange={(event) => setAi({ ...ai, address: event.target.value })}
                />
                <TextField
                  label={t("settings.model")}
                  helperText={t("companySettings.ownModelHint")}
                  value={ai.model}
                  onChange={(event) => setAi({ ...ai, model: event.target.value })}
                />
                <Box>
                  <Button
                    variant="contained"
                    disabled={ai.address.trim() === "" || ai.model.trim() === ""}
                    onClick={() => void apply({})}
                  >
                    {t("companySettings.ownSave")}
                  </Button>
                </Box>
                {refused ? <Alert severity="error">{t("companySettings.ownRefused")}</Alert> : null}
              </>
            ) : null}

            {ai.provider !== "none" ? (
              <>
                {ai.provider !== "own" ? (
                  <TextField
                    label={t("settings.model")}
                    helperText={t(ai.provider === "anthropic" ? "settings.modelHintAnthropic" : "settings.modelHintOpenAi")}
                    value={ai.model}
                    onChange={(event) => setAi({ ...ai, model: event.target.value })}
                    onBlur={() => void apply({ model: ai.model })}
                  />
                ) : null}

                <Box>
                  <TextField
                    fullWidth
                    label={t("settings.key")}
                    type="password"
                    autoComplete="off"
                    value={aiKey}
                    onChange={(event) => setAiKey(event.target.value)}
                    helperText={t(ai.provider === "own" ? "companySettings.ownKeyHint" : "companySettings.keyHint")}
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
