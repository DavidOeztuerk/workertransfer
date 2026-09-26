import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import {
  EmptyBlock,
  LoadingBlock,
  PageShell,
} from "../../../shared/components/ui";
import { useActor } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type CompanyMember,
  type Invitation,
  listInvitations,
  listMembers,
  removeMember,
  withdrawInvitation,
} from "../api/team";

/**
 * <c>/company/team</c> — who may act for the company.
 *
 * <strong>The header only hides, it does not protect.</strong> Whether someone
 * may invite or remove is decided by the server per request from the
 * membership table. What is shown or hidden here by role is convenience — no
 * access control.
 *
 * <strong>"Leave" and "remove" are the same call</strong>, only with a
 * different label: whoever removes themselves leaves. Two ways for it would be
 * two places where the last administrative role could be treated differently.
 */
export function CompanyTeamPage() {
  const { t } = useTranslation();
  const { forCompany, tenantId, subjectId } = useActor();
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const members = useAsync(
    (signal) => listMembers(tenantId as string, signal),
    [tenantId],
    forCompany,
  );

  const invitations = useAsync(
    (signal) => listInvitations(tenantId as string, signal),
    [tenantId],
    forCompany,
  );

  if (!forCompany) {
    return (
      <PageShell title={t("team.title")} narrow>
        <Card>
          <CardContent>
            <Typography>
              {t("team.companyOnly")}
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const list = members.data?.ok ? members.data.members : [];
  const myRole = list.find(
    (entry: CompanyMember) => entry.user_id === subjectId,
  )?.role;
  const isAdmin = myRole === "admin";

  const open = invitations.data?.ok ? invitations.data.invitations : [];

  async function remove(userId: string) {
    setRunning(true);
    const result = await removeMember(tenantId as string, userId);
    setRunning(false);
    setError(result.ok ? null : result.error.detail);
    members.reload();
  }

  async function withdraw(id: string) {
    setRunning(true);
    const result = await withdrawInvitation(tenantId as string, id);
    setRunning(false);
    setError(result.ok ? null : result.error.detail);
    invitations.reload();
  }

  return (
    <PageShell
      title={t("team.title")}
      narrow
      lead={t("team.lead")}
      actions={
        isAdmin ? (
          <Button
            component={RouterLink}
            to="/company/team/invite"
            variant="contained"
          >
            {t("team.invite")}
          </Button>
        ) : undefined
      }
    >
      {error !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("team.members")}
          </Typography>

          {members.pending ? (
            <LoadingBlock
              // Its own branch: without it the card stays empty while the
              // members are in flight.
              label={t("team.membersLoading")}
            />
          ) : null}

          {members.data !== null && !members.data.ok ? (
            <Alert severity="error">{members.data.error.detail}</Alert>
          ) : null}

          {list.length > 0 ? (
            <Box
              component="ul"
              sx={{
                display: "flex",
                flexDirection: "column",
                gap: 1.5,
                listStyle: "none",
                p: 0,
                m: 0,
              }}
            >
              {list.map((entry: CompanyMember) => (
                <Row
                  key={entry.user_id}
                  title={entry.display_name}
                  meta={t(
                    entry.role === "admin"
                      ? "team.roleAdmin"
                      : "team.roleMember",
                  )}
                  action={
                    isAdmin ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void remove(entry.user_id)}
                        disabled={running}
                      >
                        {t(
                          entry.user_id === subjectId
                            ? "team.leave"
                            : "team.remove",
                        )}
                      </Button>
                    ) : undefined
                  }
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            {t("team.invitations")}
          </Typography>

          {invitations.pending ? (
            <LoadingBlock label={t("team.invitationsLoading")} />
          ) : null}

          {invitations.data !== null && !invitations.data.ok ? (
            <Alert severity="error">{invitations.data.error.detail}</Alert>
          ) : null}

          {invitations.data?.ok && open.length === 0 ? (
            <EmptyBlock title={t("team.invitationsEmpty")} />
          ) : null}

          {open.length > 0 ? (
            <Box
              data-testid="invitation-list"
              sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}
            >
              {open.map((entry: Invitation) => (
                <Row
                  key={entry.id}
                  title={entry.email}
                  meta={t(
                    entry.role === "admin"
                      ? "team.roleAdmin"
                      : "team.roleMember",
                  )}
                  action={
                    isAdmin ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void withdraw(entry.id)}
                        disabled={running}
                      >
                        {t("common.withdraw")}
                      </Button>
                    ) : undefined
                  }
                />
              ))}
            </Box>
          ) : null}
        </CardContent>
      </Card>
    </PageShell>
  );
}

function Row({
  title,
  meta,
  action,
}: {
  title: string;
  meta: string;
  action?: React.ReactNode;
}) {
  return (
    <Card component="li" variant="outlined">
      <CardContent
        sx={{
          display: "flex",
          flexDirection: { xs: "column", sm: "row" },
          justifyContent: "space-between",
          alignItems: { xs: "flex-start", sm: "center" },
          gap: 2,
        }}
      >
        <Box>
          <Typography variant="h4">{title}</Typography>
          <Typography variant="body2" color="text.secondary">
            {meta}
          </Typography>
        </Box>
        {action !== undefined ? (
          <Box sx={{ flexShrink: 0 }}>{action}</Box>
        ) : null}
      </CardContent>
    </Card>
  );
}
