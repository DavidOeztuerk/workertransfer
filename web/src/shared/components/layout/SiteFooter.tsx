import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { Link as RouterLink } from "react-router-dom";

import { useAppSelector } from "../../../core/store/hooks";

/**
 * Die Fusszeile.
 *
 * <strong>Darstellung und Sprache standen hier und stehen jetzt oben</strong>,
 * im Zahnrad. Der Gedanke „das stellt man einmal ein, also gehört es nach
 * unten" war logisch und praktisch falsch: wer die Sprache wechselt, weil er
 * die Oberfläche nicht lesen kann, scrollt nicht erst an das Ende der Seite.
 *
 * <strong>Die rechtlichen Seiten sind echt und nicht nur verlinkt.</strong> Ein
 * Verweis auf ein Impressum, das es nicht gibt, ist schlechter als kein
 * Verweis: er behauptet eine Angabe, die fehlt.
 */
export function SiteFooter() {
  const { t } = useTranslation();
  const signedIn = useAppSelector((state) => state.auth.status === "authenticated");

  return (
    <Box
      component="footer"
      sx={{
        // Vorher 10 (40 px). Zusammen mit dem Seitenrand darunter klaffte
        // unter dem letzten Element — meist einer Blätterleiste — ein Loch von
        // achtundsechzig Pixeln.
        mt: 5,
        borderTop: 1,
        borderColor: "divider",
        bgcolor: "background.paper",
      }}
    >
      <Container maxWidth="lg" sx={{ py: { xs: 5, md: 7 }, px: { xs: 2, sm: 3, md: 4 } }}>
        <Box
          sx={{
            display: "grid",
            gap: { xs: 4, md: 5 },
            gridTemplateColumns: {
              xs: "1fr",
              sm: "repeat(2, 1fr)",
              md: "2fr repeat(2, 1fr)",
            },
          }}
        >
          <Box>
            <Typography variant="h4" sx={{ mb: 1 }}>
              WorkerTransfer
            </Typography>
          </Box>

          <Spalte titel={t("footer.platform")}>
            <Verweis to="/jobs">{t("footer.jobs")}</Verweis>
            {signedIn ? (
              <>
                <Verweis to="/market">{t("footer.marketStatus")}</Verweis>
                <Verweis to="/consents">{t("footer.consents")}</Verweis>
                <Verweis to="/my-data">{t("footer.myData")}</Verweis>
              </>
            ) : null}
          </Spalte>

          <Spalte titel={t("footer.legal")}>
            <Verweis to="/imprint">{t("footer.imprint")}</Verweis>
            <Verweis to="/privacy">{t("footer.privacy")}</Verweis>
            <Verweis to="/terms">{t("footer.terms")}</Verweis>
            <Verweis to="/accessibility">{t("footer.accessibility")}</Verweis>
          </Spalte>

        </Box>

        <Divider sx={{ my: { xs: 4, md: 5 } }} />

        <Box
          sx={{
            display: "flex",
            flexDirection: { xs: "column", sm: "row" },
            justifyContent: "space-between",
            alignItems: { xs: "flex-start", sm: "center" },
            gap: 1.5,
          }}
        >
          <Typography variant="caption" color="text.secondary">
            {t("footer.copyright", { jahr: new Date().getFullYear() })}
          </Typography>
        </Box>
      </Container>
    </Box>
  );
}

function Spalte({ titel, children }: { titel: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography
        variant="caption"
        sx={{
          display: "block",
          fontWeight: 660,
          letterSpacing: "0.06em",
          textTransform: "uppercase",
          color: "text.secondary",
          mb: 1.5,
        }}
      >
        {titel}
      </Typography>
      <Box sx={{ display: "flex", flexDirection: "column", alignItems: "flex-start", gap: 1 }}>
        {children}
      </Box>
    </Box>
  );
}

function Verweis({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link component={RouterLink} to={to} variant="body2" color="text.primary">
      {children}
    </Link>
  );
}
