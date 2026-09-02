import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { EmptyBlock, LoadingBlock, PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
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
 * <c>/company/team</c> — wer für das Unternehmen handeln darf.
 *
 * <strong>Die Kopfzeile verbirgt nur, sie schützt nicht.</strong> Ob jemand
 * einladen oder entfernen darf, entscheidet der Server je Anfrage aus der
 * Mitgliedschaftstabelle. Was hier nach Rolle ein- oder ausgeblendet wird, ist
 * Bequemlichkeit — kein Zugriffsschutz.
 *
 * <strong>„Verlassen" und „Entfernen" sind derselbe Aufruf</strong>, nur mit
 * anderer Beschriftung: wer sich selbst entfernt, verlässt. Zwei Wege dafür
 * wären zwei Stellen, an denen die letzte Verwaltungsrolle unterschiedlich
 * behandelt werden könnte.
 */
export function CompanyTeamPage() {
  const { fuerFirma, tenantId, subjectId } = useHandelnder();
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const mitglieder = useAsync(
    (signal) => listMembers(tenantId as string, signal),
    [tenantId],
    fuerFirma
  );

  const einladungen = useAsync(
    (signal) => listInvitations(tenantId as string, signal),
    [tenantId],
    fuerFirma
  );

  if (!fuerFirma) {
    return (
      <PageShell title="Mannschaft" narrow>
        <Card>
          <CardContent>
            <Typography>
              Die Mannschaft sieht nur, wer für ein Unternehmen handelt. Wechsle oben auf ein
              Unternehmen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  const liste = mitglieder.data?.ok ? mitglieder.data.members : [];
  const meineRolle = liste.find((eintrag: CompanyMember) => eintrag.user_id === subjectId)?.role;
  const istAdmin = meineRolle === "admin";

  const offene = einladungen.data?.ok ? einladungen.data.invitations : [];

  async function entfernen(userId: string) {
    setLaeuft(true);
    const ergebnis = await removeMember(tenantId as string, userId);
    setLaeuft(false);
    setFehler(ergebnis.ok ? null : ergebnis.error.detail);
    mitglieder.reload();
  }

  async function zurueckziehen(id: string) {
    setLaeuft(true);
    const ergebnis = await withdrawInvitation(tenantId as string, id);
    setLaeuft(false);
    setFehler(ergebnis.ok ? null : ergebnis.error.detail);
    einladungen.reload();
  }

  return (
    <PageShell
      title="Mannschaft"
      narrow
      lead={
        "Wer hier steht, kann für das Unternehmen handeln — Profile sehen, Lebensläufe anfragen. "
        + "Administratoren dürfen außerdem einladen."
      }
      actions={
        istAdmin ? (
          <Button component={RouterLink} to="/company/team/invite" variant="contained">
            Einladen
          </Button>
        ) : undefined
      }
    >
      {fehler !== null ? (
        <Alert severity="error" sx={{ mb: 2 }}>
          {fehler}
        </Alert>
      ) : null}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h2" sx={{ mb: 2 }}>
            Mitglieder
          </Typography>

          {/* Der LADEZUSTAND fehlte im alten Code, und die Karte blieb
              währenddessen leer. */}
          {mitglieder.pending ? <LoadingBlock label="Mitglieder werden geladen…" /> : null}

          {mitglieder.data !== null && !mitglieder.data.ok ? (
            <Alert severity="error">{mitglieder.data.error.detail}</Alert>
          ) : null}

          {liste.length > 0 ? (
            <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
              {liste.map((eintrag: CompanyMember) => (
                <Zeile
                  key={eintrag.user_id}
                  titel={eintrag.display_name}
                  meta={eintrag.role === "admin" ? "Administrator" : "Mitglied"}
                  aktion={
                    istAdmin ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void entfernen(eintrag.user_id)}
                        disabled={laeuft}
                      >
                        {eintrag.user_id === subjectId ? "Verlassen" : "Entfernen"}
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
            Offene Einladungen
          </Typography>

          {einladungen.pending ? <LoadingBlock label="Einladungen werden geladen…" /> : null}

          {einladungen.data !== null && !einladungen.data.ok ? (
            <Alert severity="error">{einladungen.data.error.detail}</Alert>
          ) : null}

          {einladungen.data?.ok && offene.length === 0 ? (
            <EmptyBlock title="Keine offenen Einladungen." />
          ) : null}

          {offene.length > 0 ? (
            <Box
              data-testid="invitation-list"
              sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}
            >
              {offene.map((eintrag: Invitation) => (
                <Zeile
                  key={eintrag.id}
                  titel={eintrag.email}
                  meta={eintrag.role === "admin" ? "Administrator" : "Mitglied"}
                  aktion={
                    istAdmin ? (
                      <Button
                        variant="text"
                        size="small"
                        onClick={() => void zurueckziehen(eintrag.id)}
                        disabled={laeuft}
                      >
                        Zurückziehen
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

function Zeile({
  titel,
  meta,
  aktion,
}: {
  titel: string;
  meta: string;
  aktion?: React.ReactNode;
}) {
  return (
    <Card variant="outlined">
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
          <Typography variant="h4">{titel}</Typography>
          <Typography variant="body2" color="text.secondary">
            {meta}
          </Typography>
        </Box>
        {aktion !== undefined ? <Box sx={{ flexShrink: 0 }}>{aktion}</Box> : null}
      </CardContent>
    </Card>
  );
}
