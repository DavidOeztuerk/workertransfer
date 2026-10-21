import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { Link as RouterLink } from "react-router-dom";

import { useAppSelector } from "../../../core/store/hooks";

/**
 * The footer.
 *
 * <strong>Appearance and language used to stand here and now stand at the
 * top</strong>, in the gear. The thought "one sets that once, so it belongs at
 * the bottom" was logical and practically wrong: whoever switches the language
 * because they cannot read the UI does not scroll to the end of the page first.
 *
 * <strong>The legal pages are real and not merely linked.</strong> A link to an
 * imprint that does not exist is worse than no link: it claims a statement that
 * is missing.
 */
export function SiteFooter() {
  const { t } = useTranslation();
  // The person's own pages only while acting as the person — for a company
  // "Marktstatus" asks about a job change nobody in that role is making.
  const asPerson = useAppSelector(
    (state) => state.auth.status === "authenticated" && state.auth.session?.tenantId == null,
  );

  return (
    <Box
      component="footer"
      sx={{
        // Formerly 10 (40 px). Together with the page margin below, a hole of
        // sixty-eight pixels gaped under the last element — usually a
        // pagination bar.
        mt: 5,
        borderTop: 1,
        borderColor: "divider",
        bgcolor: "background.paper",
      }}
    >
      <Container maxWidth="lg" sx={{ py: { xs: 5, md: 7 } }}>
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

          <FooterColumn title={t("footer.platform")}>
            <FooterLink to="/jobs">{t("footer.jobs")}</FooterLink>
            <FooterLink to="/checklists">{t("footer.checklists")}</FooterLink>
            <FooterLink to="/salaries">{t("footer.salaries")}</FooterLink>
            {asPerson ? (
              <>
                <FooterLink to="/market">{t("footer.marketStatus")}</FooterLink>
                <FooterLink to="/consents">{t("footer.consents")}</FooterLink>
                <FooterLink to="/my-data">{t("footer.myData")}</FooterLink>
              </>
            ) : null}
          </FooterColumn>

          <FooterColumn title={t("footer.legal")}>
            <FooterLink to="/imprint">{t("footer.imprint")}</FooterLink>
            <FooterLink to="/privacy">{t("footer.privacy")}</FooterLink>
            <FooterLink to="/terms">{t("footer.terms")}</FooterLink>
            <FooterLink to="/accessibility">{t("footer.accessibility")}</FooterLink>
          </FooterColumn>

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
            {/* `jahr` is the catalogue's placeholder name ({{jahr}}) and stays. */}
            {t("footer.copyright", { jahr: new Date().getFullYear() })}
          </Typography>
        </Box>
      </Container>
    </Box>
  );
}

function FooterColumn({ title, children }: { title: string; children: React.ReactNode }) {
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
        {title}
      </Typography>
      <Box sx={{ display: "flex", flexDirection: "column", alignItems: "flex-start", gap: 1 }}>
        {children}
      </Box>
    </Box>
  );
}

function FooterLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link component={RouterLink} to={to} variant="body2" color="text.primary">
      {children}
    </Link>
  );
}
