import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { LoadingBlock, PageShell } from "../../../shared/components/ui";
import { failureKey } from "../../../shared/api/errors";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type CompanyMember,
  type Role,
  inviteMember,
  listMembers,
} from "../api/team";

/**
 * <c>/company/team/invite</c> — invite someone.
 *
 * <strong>Only administrators may</strong>, and the check stands here too, not
 * only on the team page: whoever calls the address directly gets the same
 * sentence instead of a form the server then refuses. One's own role stands in
 * the member list, which is why this page asks for it too.
 *
 * <strong>The role check here is convenience, not protection.</strong> Whether
 * someone may invite is decided by the server per request.
 */
export function CompanyTeamInvitePage() {
  const { t } = useTranslation();
  const { forCompany, tenantId, subjectId } = useActor();

  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("member");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [running, setRunning] = useState(false);

  const members = useAsync(
    (signal) => listMembers(tenantId as string, signal),
    [tenantId],
    forCompany,
  );

  const back = (
    <Link component={RouterLink} to="/company/team" variant="body2">
      {t("team.back")}
    </Link>
  );

  const frame = (content: React.ReactNode) => (
    <PageShell title={t("team.invite")} narrow>
      <Box sx={{ mb: 2 }}>{back}</Box>
      <Card>
        <CardContent>{content}</CardContent>
      </Card>
    </PageShell>
  );

  if (!forCompany) {
    return frame(
      <Typography>
        {t("team.inviteWithoutCompany")}
      </Typography>,
    );
  }

  if (members.pending) {
    return frame(<LoadingBlock label={t("team.membersLoading")} />);
  }

  const list = members.data?.ok ? members.data.members : [];
  const isAdmin =
    list.find((entry: CompanyMember) => entry.user_id === subjectId)
      ?.role === "admin";

  if (!isAdmin) {
    return frame(
      <Typography>
        {t("team.inviteAdminOnly")}
      </Typography>,
    );
  }

  async function invite() {
    setRunning(true);
    const result = await inviteMember(tenantId as string, email, role);
    setRunning(false);

    if (result.ok) {
      setError(null);
      setSent(true);
      setEmail("");
    } else {
      // An address the server does not take is the one 422 the form can
      // produce; the status says it, the server's sentence is not shown.
      setError(
        t(
          failureKey(result.error, "errors.invitationNotCreated", [
            { status: 422, key: "team.inviteEmailInvalid" },
          ]),
        ),
      );
      setSent(false);
    }
  }

  return frame(
    <Box
      component="form"
      onSubmit={(event) => {
        event.preventDefault();
        void invite();
      }}
      sx={{
        display: "flex",
        flexDirection: "column",
        gap: 2.5,
        alignItems: "flex-start",
      }}
    >
      <TextField
        label={t("team.email")}
        type="email"
        helperText={t("team.emailHint")}
        value={email}
        onChange={(e) => {
          setEmail(e.target.value);
          setSent(false);
        }}
        required
        fullWidth
      />

      <TextField
        select
        label={t("team.role")}
        value={role}
        onChange={(e) => setRole(e.target.value as Role)}
        fullWidth
      >
        <MenuItem value="member">{t("team.roleMember")}</MenuItem>
        <MenuItem value="admin">{t("team.roleAdmin")}</MenuItem>
      </TextField>

      {error !== null ? (
        <Alert severity="error" sx={{ alignSelf: "stretch" }}>
          {error}
        </Alert>
      ) : null}

      {sent ? (
        <Alert severity="success" role="status" sx={{ alignSelf: "stretch" }}>
          {t("team.sent")}
        </Alert>
      ) : null}

      <Button type="submit" variant="contained" disabled={running}>
        {running ? t("team.sending") : t("team.invite")}
      </Button>
    </Box>,
  );
}
