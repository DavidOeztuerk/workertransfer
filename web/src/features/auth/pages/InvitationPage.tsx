import { useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useSearchParams } from "react-router-dom";

import { LoadingBlock } from "../../../shared/components/ui";
import { AuthCard } from "../components/AuthCard";
import { type Einladungsergebnis, nimmAn } from "../api/einladung";
import { Trans, useTranslation } from "react-i18next";

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
  const { t } = useTranslation();
  const [suche] = useSearchParams();
  const [lage, setLage] = useState<Lage>({ art: "laeuft" });
  const gestartet = useRef(false);

  useEffect(() => {
    if (gestartet.current) return;
    gestartet.current = true;

    const token = suche.get("token") ?? "";

    if (token === "") {
      setLage({
        art: "abgelehnt",
        brauchtKonto: false,
        meldung: t("einladung.fehltLink"),
      });
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
          : {
              art: "abgelehnt",
              brauchtKonto: ergebnis.brauchtKonto,
              meldung: ergebnis.meldung,
            },
      );
    });
  }, [suche, t]);

  if (lage.art === "laeuft") {
    return (
      <AuthCard
        title={t("einladung.laeuftTitel")}
        lead={t("einladung.zusage")}
      >
        <LoadingBlock label={t("einladung.laeuftText")} />
      </AuthCard>
    );
  }

  if (lage.art === "dabei") {
    return (
      <AuthCard
        title={t("einladung.willkommen", { name: lage.name })}
        lead={t("einladung.zusage")}
      >
        <Typography sx={{ mb: 2 }}>
          {t(
            lage.rolle === "admin"
              ? "einladung.alsAdmin"
              : "einladung.alsMitglied",
          )}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          <Trans
            i18nKey="einladung.wechselHinweis"
            components={{ 1: <Link component={RouterLink} to="/" /> }}
          />
        </Typography>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title={t("einladung.abgelehntTitel")}
      lead={t("einladung.zusage")}
    >
      <Alert severity="error" sx={{ mb: 2 }}>
        {lage.meldung}
      </Alert>
      <Box>
        {lage.brauchtKonto ? (
          <Typography variant="body2">
            <Trans
              i18nKey="einladung.brauchtKonto"
              components={{
                1: <Link component={RouterLink} to="/login" />,
                3: <Link component={RouterLink} to="/register" />,
              }}
            />
          </Typography>
        ) : (
          <Link component={RouterLink} to="/" variant="body2">
            {t("einladung.zurStartseite")}
          </Link>
        )}
      </Box>
    </AuthCard>
  );
}
