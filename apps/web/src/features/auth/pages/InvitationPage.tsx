import { useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { AuthCard } from "../components/AuthCard";
import { type Einladungsergebnis, nimmAn } from "../api/einladung";

const ZUSAGE =
  "Die Einladung gilt genau für die Adresse, an die sie ging — ein weitergeleiteter Link "
  + "öffnet nichts.";

type Lage =
  | { art: "laeuft" }
  | { art: "dabei"; name: string; rolle: string }
  | { art: "abgelehnt"; brauchtKonto: boolean; meldung: string };

/**
 * <c>/invitation</c> — einen Einladungslink einlösen.
 *
 * <strong>Genau einmal.</strong> Ein Einladungstoken ist einmalig; wird er
 * zweimal eingelöst, scheitert der zweite Versuch und die Seite zeigte einen
 * Fehler für etwas, das gerade erfolgreich war. React 19 ruft Effekte im
 * StrictMode doppelt — ohne die Sperre wäre das der Normalfall, nicht die
 * Ausnahme.
 *
 * <strong>Es wird NICHT von selbst umgeschaltet.</strong> Wer beitritt, handelt
 * danach weiter als er selbst und wählt das Unternehmen bewusst oben aus. Von
 * selbst zu wechseln hiesse, jemanden ungefragt aus dem Unternehmen
 * herauszuholen, in dem er gerade arbeitet.
 */
export function InvitationPage() {
  const [suche] = useSearchParams();
  const [lage, setLage] = useState<Lage>({ art: "laeuft" });
  const gestartet = useRef(false);

  useEffect(() => {
    if (gestartet.current) return;
    gestartet.current = true;

    const token = suche.get("token") ?? "";

    if (token === "") {
      setLage({ art: "abgelehnt", brauchtKonto: false, meldung: "Es fehlt ein Einladungslink." });
      return;
    }

    void nimmAn(token).then((ergebnis: Einladungsergebnis) => {
      setLage(
        ergebnis.ok
          ? {
              art: "dabei",
              name: ergebnis.mitgliedschaft.name,
              rolle: ergebnis.mitgliedschaft.role,
            }
          : { art: "abgelehnt", brauchtKonto: ergebnis.brauchtKonto, meldung: ergebnis.meldung }
      );
    });
  }, [suche]);

  if (lage.art === "laeuft") {
    return (
      <AuthCard title="Einladung wird geprüft…" lead={ZUSAGE}>
        <LoadingBlock label="Einen Moment bitte." />
      </AuthCard>
    );
  }

  if (lage.art === "dabei") {
    return (
      <AuthCard title={`Willkommen bei ${lage.name}`} lead={ZUSAGE}>
        <Typography sx={{ mb: 2 }}>
          {lage.rolle === "admin"
            ? "Du bist Administrator dieses Unternehmens."
            : "Du bist Mitglied dieses Unternehmens."}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Um dafür zu handeln, wähle das Unternehmen oben aus. Wir wechseln nicht von selbst —
          sonst würdest du ungefragt aus dem Unternehmen herausgeholt, in dem du gerade arbeitest.{" "}
          <Link component={RouterLink} to="/">
            Zur Startseite
          </Link>
        </Typography>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Einladung nicht angenommen" lead={ZUSAGE}>
      <Alert severity="error" sx={{ mb: 2 }}>
        {lage.meldung}
      </Alert>
      <Box>
        {lage.brauchtKonto ? (
          <Typography variant="body2">
            <Link component={RouterLink} to="/login">
              Anmelden
            </Link>{" "}
            — oder zuerst{" "}
            <Link component={RouterLink} to="/register">
              Registrieren
            </Link>{" "}
            und den Link danach erneut öffnen.
          </Typography>
        ) : (
          <Link component={RouterLink} to="/" variant="body2">
            Zur Startseite
          </Link>
        )}
      </Box>
    </AuthCard>
  );
}
