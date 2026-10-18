import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { useSearchParams } from "react-router-dom";

import { failureKey, type MessageKey } from "../../../shared/api/errors";
import { useAsync } from "../../person/lib/useAsync";
import { type ExternalProvider, UNLINK_CASES, getLinks, outcomeOf, startAddress, unlink } from "../api/externalLogin";
import { PROVIDER_KEYS } from "./ExternalSignIn";

/**
 * The account's ways in (ADR-0053): password and providers, on the settings page.
 *
 * <strong>Binding a provider happens here and only here</strong> — signed in.
 * At the login an address that already has an account is never linked: whoever
 * could enter a foreign address at a provider would otherwise take the account
 * over. And the last way in cannot be taken off.
 */
export function SignInWays() {
  const { t, i18n } = useTranslation();
  const [params] = useSearchParams();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<MessageKey | null>(null);
  const links = useAsync((signal) => getLinks(signal), []);
  const outcome = outcomeOf(params.get("external"));
  const state = links.value?.ok ? links.value.links : null;

  if (state === null || (state.available.length === 0 && state.linked.length === 0)) {
    return null;
  }

  const linkedProviders = new Set(state.linked.map((entry) => entry.provider));

  async function takeOff(provider: ExternalProvider) {
    setBusy(true);
    const result = await unlink(provider);
    setBusy(false);
    setError(result.ok ? null : failureKey(result.error, "errors.stepFailed", UNLINK_CASES));
    links.again();
  }

  return (
    <Card component="section" aria-label={t("externalLogin.waysTitle")} sx={{ mt: 3 }}>
      <CardContent sx={{ display: "grid", gap: 1.5 }}>
        <Typography variant="h2">{t("externalLogin.waysTitle")}</Typography>
        {outcome !== null ? <Alert severity={outcome.severity}>{t(outcome.key)}</Alert> : null}
        {error !== null ? <Alert severity="error">{t(error)}</Alert> : null}

        <Typography variant="body2">
          {state.has_password ? t("externalLogin.passwordSet") : t("externalLogin.passwordNone")}
        </Typography>

        {state.linked.map((entry) => (
          <Box key={entry.provider} sx={{ display: "flex", alignItems: "center", gap: 1, flexWrap: "wrap" }}>
            <Typography variant="body2" sx={{ flexGrow: 1 }}>
              {t("externalLogin.linkedLine", {
                provider: t(PROVIDER_KEYS[entry.provider]),
                date: new Date(entry.created_at).toLocaleDateString(i18n.language),
              })}
            </Typography>
            <Button size="small" disabled={busy} onClick={() => void takeOff(entry.provider)}>
              {t("externalLogin.unlink")}
            </Button>
          </Box>
        ))}

        <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
          {state.available
            .filter((provider) => !linkedProviders.has(provider))
            .map((provider) => (
              <Button key={provider} variant="outlined" size="small" href={startAddress(provider, "link")}>
                {t("externalLogin.link", { provider: t(PROVIDER_KEYS[provider]) })}
              </Button>
            ))}
        </Box>
      </CardContent>
    </Card>
  );
}
