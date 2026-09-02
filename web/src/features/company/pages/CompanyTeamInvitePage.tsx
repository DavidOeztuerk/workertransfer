import { useState } from "react";
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
import { type CompanyMember, type Role, inviteMember, listMembers } from "../api/team";

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
  const { fuerFirma, tenantId, subjectId } = useHandelnder();

  const [email, setEmail] = useState("");
  const [rolle, setRolle] = useState<Role>("member");
  const [fehler, setFehler] = useState<string | null>(null);
  const [verschickt, setVerschickt] = useState(false);
  const [laeuft, setLaeuft] = useState(false);

  const mitglieder = useAsync(
    (signal) => listMembers(tenantId as string, signal),
    [tenantId],
    fuerFirma
  );

  const zurueck = (
    <Link component={RouterLink} to="/company/team" variant="body2">
      Zurück zur Mannschaft
    </Link>
  );

  const rahmen = (inhalt: React.ReactNode) => (
    <PageShell title="Einladen" narrow>
      <Box sx={{ mb: 2 }}>{zurueck}</Box>
      <Card>
        <CardContent>{inhalt}</CardContent>
      </Card>
    </PageShell>
  );

  if (!fuerFirma) {
    return rahmen(
      <Typography>
        Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen einladen.
      </Typography>
    );
  }

  if (mitglieder.pending) {
    return rahmen(<LoadingBlock label="Mitglieder werden geladen…" />);
  }

  const liste = mitglieder.data?.ok ? mitglieder.data.members : [];
  const istAdmin =
    liste.find((eintrag: CompanyMember) => eintrag.user_id === subjectId)?.role === "admin";

  if (!istAdmin) {
    return rahmen(
      <Typography>
        Einladen dürfen nur Administratoren. Wende dich an jemanden aus deinem Unternehmen, der
        das ist.
      </Typography>
    );
  }

  async function einladen() {
    setLaeuft(true);
    const ergebnis = await inviteMember(tenantId as string, email, rolle);
    setLaeuft(false);

    if (ergebnis.ok) {
      setFehler(null);
      setVerschickt(true);
      setEmail("");
    } else {
      setFehler(ergebnis.error.detail);
      setVerschickt(false);
    }
  }

  return rahmen(
    <Box
      component="form"
      onSubmit={(ereignis) => {
        ereignis.preventDefault();
        void einladen();
      }}
      sx={{ display: "flex", flexDirection: "column", gap: 2.5, alignItems: "flex-start" }}
    >
      <TextField
        label="E-Mail-Adresse"
        type="email"
        helperText="Die Person braucht ein Konto mit genau dieser Adresse — sie kann es auch nach der Einladung anlegen."
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
        label="Rolle"
        value={rolle}
        onChange={(e) => setRolle(e.target.value as Role)}
        fullWidth
      >
        <MenuItem value="member">Mitglied</MenuItem>
        <MenuItem value="admin">Administrator</MenuItem>
      </TextField>

      {fehler !== null ? (
        <Alert severity="error" sx={{ alignSelf: "stretch" }}>
          {fehler}
        </Alert>
      ) : null}

      {verschickt ? (
        <Alert severity="success" role="status" sx={{ alignSelf: "stretch" }}>
          Einladung verschickt.
        </Alert>
      ) : null}

      <Button type="submit" variant="contained" disabled={laeuft}>
        {laeuft ? "Wird verschickt…" : "Einladen"}
      </Button>
    </Box>
  );
}
