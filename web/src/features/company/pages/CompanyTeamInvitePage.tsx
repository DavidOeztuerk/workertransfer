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
import { useHandelnder } from "../lib/session";
import { useAsync } from "../lib/useAsync";
import {
  type CompanyMember,
  type Role,
  inviteMember,
  listMembers,
} from "../api/team";

/**
 * <c>/company/team/invite</c> — jemanden einladen.
 *
 * <strong>Nur Administratoren dürfen es</strong>, und die Prüfung steht auch
 * hier, nicht nur auf der Mannschaftsseite: wer die Adresse direkt aufruft,
 * bekommt denselben Satz statt eines Formulars, das der Server anschliessend
 * ablehnt. Die eigene Rolle steht in der Mitgliederliste, deshalb fragt auch
 * diese Seite sie ab.
 *
 * <strong>Die Rollenprüfung hier ist Bequemlichkeit, kein Schutz.</strong> Ob
 * jemand einladen darf, entscheidet der Server je Anfrage.
 */
export function CompanyTeamInvitePage() {
  const { t } = useTranslation();
  const { fuerFirma, tenantId, subjectId } = useHandelnder();

  const [email, setEmail] = useState("");
  const [rolle, setRolle] = useState<Role>("member");
  const [fehler, setFehler] = useState<string | null>(null);
  const [sent, setVerschickt] = useState(false);
  const [running, setLaeuft] = useState(false);

  const mitglieder = useAsync(
    (signal) => listMembers(tenantId as string, signal),
    [tenantId],
    fuerFirma,
  );

  const back = (
    <Link component={RouterLink} to="/company/team" variant="body2">
      {t("mannschaft.zurueck")}
    </Link>
  );

  const rahmen = (content: React.ReactNode) => (
    <PageShell title={t("mannschaft.einladen")} narrow>
      <Box sx={{ mb: 2 }}>{back}</Box>
      <Card>
        <CardContent>{content}</CardContent>
      </Card>
    </PageShell>
  );

  if (!fuerFirma) {
    return rahmen(
      <Typography>
        {t("mannschaft.einladenOhneFirma")}
      </Typography>,
    );
  }

  if (mitglieder.pending) {
    return rahmen(<LoadingBlock label={t("mannschaft.mitgliederLaden")} />);
  }

  const list = mitglieder.data?.ok ? mitglieder.data.members : [];
  const istAdmin =
    list.find((entry: CompanyMember) => entry.user_id === subjectId)
      ?.role === "admin";

  if (!istAdmin) {
    return rahmen(
      <Typography>
        {t("mannschaft.einladenNurAdmin")}
      </Typography>,
    );
  }

  async function einladen() {
    setLaeuft(true);
    const result = await inviteMember(tenantId as string, email, rolle);
    setLaeuft(false);

    if (result.ok) {
      setFehler(null);
      setVerschickt(true);
      setEmail("");
    } else {
      setFehler(result.error.detail);
      setVerschickt(false);
    }
  }

  return rahmen(
    <Box
      component="form"
      onSubmit={(event) => {
        event.preventDefault();
        void einladen();
      }}
      sx={{
        display: "flex",
        flexDirection: "column",
        gap: 2.5,
        alignItems: "flex-start",
      }}
    >
      <TextField
        label={t("mannschaft.email")}
        type="email"
        helperText={t("mannschaft.emailHinweis")}
        value={email}
        onChange={(e) => {
          setEmail(e.target.value);
          setVerschickt(false);
        }}
        required
        fullWidth
      />

      <TextField
        select
        label={t("mannschaft.rolle")}
        value={rolle}
        onChange={(e) => setRolle(e.target.value as Role)}
        fullWidth
      >
        <MenuItem value="member">{t("mannschaft.rolleMitglied")}</MenuItem>
        <MenuItem value="admin">{t("mannschaft.rolleAdmin")}</MenuItem>
      </TextField>

      {fehler !== null ? (
        <Alert severity="error" sx={{ alignSelf: "stretch" }}>
          {fehler}
        </Alert>
      ) : null}

      {sent ? (
        <Alert severity="success" role="status" sx={{ alignSelf: "stretch" }}>
          {t("mannschaft.verschickt")}
        </Alert>
      ) : null}

      <Button type="submit" variant="contained" disabled={running}>
        {running ? t("mannschaft.verschicktLaeuft") : t("mannschaft.einladen")}
      </Button>
    </Box>,
  );
}
