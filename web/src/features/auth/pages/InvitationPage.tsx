import { useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { AuthCard } from "../components/AuthCard";
import { type InvitationResult, acceptInvitation } from "../api/invitation";
import { Trans, useTranslation } from "react-i18next";

type InvitationState =
  | { kind: "running" }
  | { kind: "joined"; name: string; role: string }
  | { kind: "declined"; needsAccount: boolean; message: string };

/**
 * <c>/invitation</c> — redeem an invitation link.
 *
 * <strong>Exactly once.</strong> An invitation token is single-use; redeemed
 * twice, the second attempt fails and the page showed an error for something
 * that had just succeeded. React 19 calls effects twice in StrictMode — without
 * the lock that would be the normal case, not the exception.
 *
 * <strong>There is NO automatic switch.</strong> Whoever joins keeps acting as
 * themselves afterwards and picks the company deliberately at the top.
 * Switching by itself would mean pulling someone, unasked, out of the company
 * they are working in right now.
 */
export function InvitationPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const [state, setState] = useState<InvitationState>({ kind: "running" });
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const token = searchParams.get("token") ?? "";

    if (token === "") {
      setState({
        kind: "declined",
        needsAccount: false,
        message: t("invitation.linkMissing"),
      });
      return;
    }

    void acceptInvitation(token).then((result: InvitationResult) => {
      setState(
        result.ok
          ? {
              kind: "joined",
              name: result.membership.name,
              role: result.membership.role,
            }
          : {
              kind: "declined",
              needsAccount: result.needsAccount,
              message: result.message,
            },
      );
    });
  }, [searchParams, t]);

  if (state.kind === "running") {
    return (
      <AuthCard
        title={t("invitation.inProgressTitle")}
        lead={t("invitation.promise")}
      >
        <LoadingBlock label={t("invitation.inProgressText")} />
      </AuthCard>
    );
  }

  if (state.kind === "joined") {
    return (
      <AuthCard
        title={t("invitation.welcome", { name: state.name })}
        lead={t("invitation.promise")}
      >
        <Typography sx={{ mb: 2 }}>
          {t(
            state.role === "admin"
              ? "invitation.asAdmin"
              : "invitation.asMember",
          )}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="invitation.switchHint"
            components={{ 1: <Link component={RouterLink} to="/" /> }}
          />
        </Typography>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title={t("invitation.declinedTitle")}
      lead={t("invitation.promise")}
    >
      <Alert severity="error" sx={{ mb: 2 }}>
        {state.message}
      </Alert>
      <Box>
        {state.needsAccount ? (
          <Typography variant="body2">
            <Trans
              i18nKey="invitation.needsAccount"
              components={{
                1: <Link component={RouterLink} to="/login" />,
                3: <Link component={RouterLink} to="/register" />,
              }}
            />
          </Typography>
        ) : (
          <Link component={RouterLink} to="/" variant="body2">
            {t("invitation.toHome")}
          </Link>
        )}
      </Box>
    </AuthCard>
  );
}
