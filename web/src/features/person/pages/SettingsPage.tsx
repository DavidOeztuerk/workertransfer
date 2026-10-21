import { useState } from "react";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";
import { Link as RouterLink } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import MenuItem from "@mui/material/MenuItem";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";

import {
  ConsentSwitch,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useAppSelector } from "../../../core/store/hooks";
import { AppearanceSections } from "../../../shared/components/layout/appearance";
import { SignInRequired } from "../components/SignInRequired";
import { CivilIdentity } from "../components/CivilIdentity";
import { SignInWays } from "../../auth/components/SignInWays";
import { useAsync } from "../lib/useAsync";
import {
  type AccountSettings,
  type AiProvider,
  getAccountSettings,
  saveAccountSettings,
  saveAiKey,
} from "../api/accountSettings";
import { failureKey } from "../../../shared/api/errors";
import {
  ALL_ON,
  type NotificationPreferences,
  getNotificationPreferences,
  saveNotificationPreferences,
} from "../api/notificationPreferences";

/**
 * The switches. The fifth is an application arriving at the company, the sixth
 * the discovery of one's own profile in a search.
 *
 * The hint under each says what switching it off <em>costs</em>, not what the
 * switch does. Whoever switches off "market status requests" should know that
 * they then learn of the request only if they happen to drop by.
 */
const SWITCHES: {
  key: keyof NotificationPreferences;
  label: string;
  hint: string;
  /** Only for somebody who belongs to a company — a person alone has no inbox there. */
  companyOnly?: boolean;
}[] = [
  { key: "market_request", label: "settings.marketLabel", hint: "settings.marketHint" },
  { key: "resume_request", label: "settings.resumeLabel", hint: "settings.resumeHint" },
  { key: "transfer_update", label: "settings.transferLabel", hint: "settings.transferHint" },
  { key: "application_update", label: "settings.applicationLabel", hint: "settings.applicationHint" },
  { key: "application_received", label: "settings.receivedLabel", hint: "settings.receivedHint", companyOnly: true },
  { key: "profile_discovered", label: "settings.discoveredLabel", hint: "settings.discoveredHint" },
  { key: "advisor_conversation", label: "settings.advisorLabel", hint: "settings.advisorHint" },
  { key: "assessment_update", label: "settings.assessmentLabel", hint: "settings.assessmentHint" },
  { key: "message_received", label: "settings.messageLabel", hint: "settings.messageHint" },
];

/**
 * <c>/settings</c> — what a mail may say.
 *
 * <strong>The text at the top is the page's actual statement</strong> and was
 * taken over verbatim: a mail says only <em>that</em> there is something new.
 * No company name, no case, no count. A mail may land in an inbox that does not
 * belong to the person alone — and then the sentence that would make it more
 * useful is exactly the one that costs somebody their job.
 *
 * <strong>Unknown is not off.</strong> If the service does not answer, the
 * switches stand at their default but are <em>locked</em>. Whoever could
 * operate them in that state would save a setting they never made — and might
 * overwrite one they once made.
 */
export function SettingsPage() {
  const { t } = useTranslation();
  const session = useAppSelector((state) => state.auth.session);
  const status = useAppSelector((state) => state.auth.status);
  const hasCompany = useAppSelector((state) => state.auth.memberships.length > 0);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const choice = useAsync(
    (signal) => getNotificationPreferences(signal),
    [session?.userId],
    session !== null,
  );

  if (status === "anonymous") {
    return (
      <SignInRequired
        title={t("settings.title")}
        sentenceKey="settings.signInRequired"
      />
    );
  }

  const stateUnknown = choice.loading || choice.value === null;
  const values = choice.value ?? ALL_ON;

  async function toggle(
    key: keyof NotificationPreferences,
    next: boolean,
  ) {
    const nextValue = { ...values, [key]: next };
    setSaving(true);
    choice.set(nextValue);

    const result = await saveNotificationPreferences(nextValue);
    setSaving(false);

    if (result.ok) {
      setError(null);
      choice.set(result.choice);
    } else {
      setError(t(failureKey(result.error, "errors.settingsNotSaved")));
      // Back to what the service really has — the display must not claim a
      // setting that was not saved.
      choice.again();
    }
  }

  return (
    <PageShell
      title={t("settings.title")}
      narrow
      lead={t("settings.lead")}
    >
      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("settings.notifications")}
          </Typography>

          {error !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          ) : null}

          {choice.loading ? (
            <LoadingBlock label={t("settings.loading")} />
          ) : null}

          {choice.value === null && !choice.loading ? (
            <Alert severity="warning" sx={{ mb: 2 }}>
              {t("settings.unavailable")}
            </Alert>
          ) : null}

          <Box sx={{ display: "flex", flexDirection: "column", gap: 2 }}>
            {SWITCHES.filter((entry) => !entry.companyOnly || hasCompany).map((entry) => (
              <ConsentSwitch
                key={entry.key}
                label={t(entry.label)}
                hint={t(entry.hint)}
                checked={values[entry.key]}
                disabled={saving || stateUnknown}
                onChange={(next) => void toggle(entry.key, next)}
              />
            ))}
          </Box>

          <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
            {t("settings.rate")}
          </Typography>
        </CardContent>
      </Card>

      <AppearanceBlock />
      <CivilIdentity />
      <SignInWays />
      <PrivacyBlock />
      <AiBlock />
      <RecordsBlock />
    </PageShell>
  );
}

/**
 * Appearance and language, in full. The account menu carries the short
 * version; both read `usePreferences`.
 */
function AppearanceBlock() {
  const { t } = useTranslation();

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 2.5 }}>
          {t("settings.appearanceTitle")}
        </Typography>
        <AppearanceSections />
      </CardContent>
    </Card>
  );
}

/** How long the account persists without a sign-in. */
function PrivacyBlock() {
  const { t } = useTranslation();
  const current = useAsync((signal) => getAccountSettings(signal), []);
  const [saved, setSaved] = useState(false);

  const settings = current.value ?? null;

  async function apply(months: number | null) {
    if (settings === null) return;
    const result = await saveAccountSettings({ ...settings, deleteAfterMonths: months });
    if (result.ok) {
      current.set(result.settings);
      setSaved(true);
    }
  }

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 2 }}>
          {t("settings.privacy")}
        </Typography>

        {current.loading ? <LoadingBlock label={t("settings.loading")} /> : null}

        {settings !== null ? (
          <>
            <TextField
              select
              label={t("settings.expiry")}
              value={String(settings.deleteAfterMonths ?? "")}
              helperText={t("settings.expiryHint")}
              onChange={(event) =>
                void apply(event.target.value === "" ? null : Number(event.target.value))
              }
              sx={{ maxWidth: 360 }}
            >
              <MenuItem value="">{t("settings.expiryNever")}</MenuItem>
              {[6, 12, 24, 36].map((months) => (
                <MenuItem key={months} value={months}>
                  {t("settings.expiryMonths", { count: months })}
                </MenuItem>
              ))}
            </TextField>

            {saved ? (
              <Alert severity="success" role="status" sx={{ mt: 2 }}>
                {t("settings.savedShort")}
              </Alert>
            ) : null}
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}

/**
 * Who may be asked — and the key, which only goes in.
 *
 * <strong>The key has a field and a button of its own.</strong> It never comes
 * back, so it cannot stand in the form at all; saving it together with the
 * rest would mean sending it over the wire again at every save.
 *
 * <strong>No address field.</strong> The address belongs to the provider and
 * is set by the platform (ADR-0051): a typed one would be a way to send a
 * person's texts to any server. Without a stored key the platform's model
 * keeps drafting, and the page says so instead of leaving her guessing.
 */
function AiBlock() {
  const { t } = useTranslation();
  const current = useAsync((signal) => getAccountSettings(signal), []);
  const [aiKey, setAiKey] = useState("");
  const [saved, setSaved] = useState(false);

  const settings = current.value ?? null;

  async function apply(part: Partial<AccountSettings>) {
    if (settings === null) return;
    const result = await saveAccountSettings({ ...settings, ...part });
    if (result.ok) {
      current.set(result.settings);
      setSaved(true);
    }
  }

  async function storeAiKey() {
    if (await saveAiKey(aiKey)) {
      setAiKey("");
      current.again();
      setSaved(true);
    }
  }

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("settings.ai")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("settings.aiLead")}
        </Typography>

        {current.loading ? <LoadingBlock label={t("settings.loading")} /> : null}

        {settings !== null ? (
          <Box sx={{ display: "grid", gap: 2.5 }}>
            <TextField
              select
              label={t("settings.provider")}
              value={settings.provider ?? "none"}
              helperText={t("settings.providerHint")}
              onChange={(event) => void apply({ provider: event.target.value as AiProvider })}
            >
              <MenuItem value="none">{t("settings.providerNone")}</MenuItem>
              <MenuItem value="anthropic">{t("settings.providerAnthropic")}</MenuItem>
              <MenuItem value="openai">{t("settings.providerOpenAi")}</MenuItem>
            </TextField>

            {settings.provider !== "none" ? (
              <>
                <TextField
                  label={t("settings.model")}
                  helperText={t(settings.provider === "anthropic" ? "settings.modelHintAnthropic" : "settings.modelHintOpenAi")}
                  value={settings.model ?? ""}
                  onChange={(event) =>
                    current.set({ ...settings, model: event.target.value })
                  }
                  onBlur={() => void apply({ model: settings.model ?? "" })}
                />

                <Box>
                    <TextField
                      label={t("settings.key")}
                      type="password"
                      autoComplete="off"
                      value={aiKey}
                      onChange={(event) => setAiKey(event.target.value)}
                      helperText={t("settings.keyHint")}
                    />
                    <Box sx={{ display: "flex", gap: 1.5, mt: 1.5, alignItems: "center" }}>
                      <Button variant="outlined" onClick={() => void storeAiKey()}>
                        {t("settings.keySave")}
                      </Button>
                      <Typography variant="body2" color="text.secondary">
                        {settings.keyPresent
                          ? t("settings.keyStored", { endung: settings.keyTail })
                          : t("settings.keyNone")}
                      </Typography>
                    </Box>
                  </Box>

                <ConsentSwitch
                  label={t("settings.aiLog")}
                  hint={t("settings.aiLogHint")}
                  checked={settings.auditLog === true}
                  onChange={(next) => void apply({ auditLog: next })}
                />
              </>
            ) : null}

            {saved ? (
              <Alert severity="success" role="status">
                {t("settings.savedShort")}
              </Alert>
            ) : null}
          </Box>
        ) : null}
      </CardContent>
    </Card>
  );
}

/**
 * The ways to what is recorded about oneself.
 *
 * The closing paragraph names what is NOT possible here: a settings page
 * that keeps a gap quiet looks complete and is not.
 */
function RecordsBlock() {
  const { t } = useTranslation();
  const hasCompany = useAppSelector((state) => state.auth.memberships.length > 0);

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("settings.records")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          {t("settings.recordsLead")}
        </Typography>
        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          <Button component={RouterLink} to="/consents" variant="outlined">
            {t("settings.recordsConsents")}
          </Button>
          <Button component={RouterLink} to="/my-data" variant="outlined">
            {t("settings.recordsData")}
          </Button>
          <Button
            component={RouterLink}
            // Deletion belongs where people manage their account.
            to="/delete-account"
            // Outlined: findable without being the obvious action.
            variant="outlined"
            color="error"
          >
            {t("header.deleteAccount")}
          </Button>
        </Box>

        {hasCompany ? (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>
            <strong>{t("settings.company")}.</strong> {t("settings.companyLead")}
          </Typography>
        ) : null}
      </CardContent>
    </Card>
  );
}

