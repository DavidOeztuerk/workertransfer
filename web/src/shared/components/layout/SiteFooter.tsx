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
        mt: 10,
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
            <Typography variant="body2" color="text.secondary" sx={{ maxWidth: "34ch" }}>
              {t("fuss.anspruch")}
            </Typography>
          </Box>

          <Spalte titel={t("fuss.plattform")}>
            <Verweis to="/jobs">{t("fuss.stellen")}</Verweis>
            {signedIn ? (
              <>
                <Verweis to="/market">{t("fuss.marktstatus")}</Verweis>
                <Verweis to="/consents">{t("fuss.freigaben")}</Verweis>
                <Verweis to="/my-data">{t("fuss.meineDaten")}</Verweis>
              </>
            ) : null}
          </Spalte>

          <Spalte titel={t("fuss.rechtliches")}>
            <Verweis to="/imprint">{t("fuss.impressum")}</Verweis>
            <Verweis to="/privacy">{t("fuss.datenschutz")}</Verweis>
            <Verweis to="/terms">{t("fuss.agb")}</Verweis>
            <Verweis to="/accessibility">{t("fuss.barrierefreiheit")}</Verweis>
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
            {t("fuss.rechte", { jahr: new Date().getFullYear() })}
          </Typography>
          {/* Die drei Sätze, auf denen die Plattform steht — als Zusage und
              nicht als Werbung. Sie stehen unter jeder Seite, weil sie unter
              jeder Seite gelten. */}
          <Typography variant="caption" color="text.secondary" sx={{ maxWidth: "68ch" }}>
            {t("fuss.zusage")}
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
