import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
import Typography from "@mui/material/Typography";
import { useSearchParams } from "react-router-dom";

import type { MessageKey } from "../../../shared/api/errors";
import { type ExternalProvider, getProviders, outcomeOf, startAddress } from "../api/externalLogin";

export const PROVIDER_KEYS: Record<ExternalProvider, MessageKey> = {
  google: "externalLogin.google",
  microsoft: "externalLogin.microsoft",
  apple: "externalLogin.apple",
};

/**
 * "Or sign in with …" under the login and registration forms (ADR-0053).
 *
 * <strong>Only the buttons this server can keep.</strong> A provider without
 * credentials draws none — the list comes from the server, so nothing here
 * promises a way that leads nowhere. What a returning flow says
 * (`?external=…`) stands above them.
 */
export function ExternalSignIn({ register = false }: { register?: boolean }) {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const [providers, setProviders] = useState<ExternalProvider[]>([]);
  const outcome = outcomeOf(params.get("external"));

  useEffect(() => {
    const controller = new AbortController();
    void getProviders(controller.signal).then((found) => {
      if (!controller.signal.aborted) setProviders(found);
    });
    return () => controller.abort();
  }, []);

  if (providers.length === 0 && outcome === null) return null;

  return (
    <Box sx={{ display: "grid", gap: 1.5, mt: 3 }}>
      {outcome !== null ? <Alert severity={outcome.severity}>{t(outcome.key)}</Alert> : null}
      {providers.length > 0 ? (
        <>
          <Divider>{t("externalLogin.or")}</Divider>
          {providers.map((provider) => (
            <Button key={provider} variant="outlined" size="large" href={startAddress(provider)}>
              {t(register ? "externalLogin.registerWith" : "externalLogin.signInWith", { provider: t(PROVIDER_KEYS[provider]) })}
            </Button>
          ))}
          <Typography variant="caption" color="text.secondary">
            {t("externalLogin.hint")}
          </Typography>
        </>
      ) : null}
    </Box>
  );
}
