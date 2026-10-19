import { useId } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import { alpha } from "@mui/material/styles";
import { Link as RouterLink, Navigate } from "react-router-dom";

import { useAppSelector } from "../../../core/store/hooks";
import { scale } from "../../../styles/tokens/typography";

/**
 * The three sentences the platform stands on — only their keys stand here, the
 * wording lies in the catalogues.
 */
const PRINCIPLES = [1, 2, 3] as const;

/**
 * The marketing page at `/` — and the redirect of the signed-in to
 * `/overview`.
 *
 * <strong>Two addresses, not one.</strong> Before, `/` served both: the
 * advertising signed out, the overview signed in. That kept the advertising out
 * of the way but gave the overview no address of its own — it was not
 * linkable, and a screenshot of `/` showed something different depending on
 * the session.
 *
 * As long as the session is still unknown, NOTHING is drawn. That is the
 * difference between a short empty area and the jump advertising → overview,
 * which looks like a defect.
 */
export function HomePage() {
  const { t } = useTranslation();
  const status = useAppSelector((state) => state.auth.status);

  const heroId = useId();
  const principlesId = useId();
  const roadmapId = useId();

  if (status === "authenticated") return <Navigate to="/overview" replace />;
  if (status === "unknown") return null;

  return (
    <Box>
      <Box
        component="section"
        aria-labelledby={heroId}
        sx={{
          position: "relative",
          overflow: "hidden",
          borderBottom: 1,
          borderColor: "divider",
          // The gradient comes from the palette, not from literals: a second
          // colour source drifts apart in dark mode.
          background: (theme) =>
            `radial-gradient(1200px 480px at 12% -10%, ${alpha(
              theme.palette.primary.main,
              theme.palette.mode === "light" ? 0.14 : 0.22,
            )}, transparent 62%), radial-gradient(900px 420px at 88% 4%, ${alpha(
              theme.palette.secondary.main,
              theme.palette.mode === "light" ? 0.1 : 0.16,
            )}, transparent 58%)`,
        }}
      >
        <Container maxWidth="lg" sx={{ py: { xs: 8, md: 14 } }}>
          <Box sx={{ maxWidth: "62ch" }}>
            <Typography
              variant="body2"
              sx={{
                color: "primary.main",
                fontWeight: 620,
                letterSpacing: "0.08em",
                textTransform: "uppercase",
                mb: 2,
              }}
            >
              {t("home.eyebrow")}
            </Typography>
            <Typography
              id={heroId}
              variant="h1"
              sx={{
                ...scale.display,
                fontSize: {
                  xs: "2.15rem",
                  sm: "2.6rem",
                  md: scale.display.fontSize,
                },
                mb: 3,
              }}
            >
              {t("home.title")}
            </Typography>
            <Typography
              variant="body1"
              color="text.secondary"
              sx={{ fontSize: "1.0625rem", mb: 4 }}
            >
              {t("home.text")}
            </Typography>
            <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1.5 }}>
              <Button
                component={RouterLink}
                to="/register"
                variant="contained"
                size="large"
              >
                {t("home.asPerson")}
              </Button>
              <Button
                component={RouterLink}
                // Carries the INTENT: without `?as=company` whoever clicks
                // "discover as a company" lands in the person form and notices
                // only after the confirmation mail, when no company is there.
                // The distinction itself is drawn at registration, not here: a
                // company needs a CONFIRMED address (ADR-0019), and the account
                // is still pending at this point.
                to="/register?as=company"
                variant="outlined"
                size="large"
              >
                {t("home.asCompany")}
              </Button>
            </Box>
          </Box>
        </Container>
      </Box>

      <Container maxWidth="lg" sx={{ py: { xs: 7, md: 12 } }}>
        <Box component="section" aria-labelledby={principlesId}>
          <Typography
            variant="body2"
            sx={{
              color: "text.secondary",
              fontWeight: 620,
              letterSpacing: "0.08em",
              textTransform: "uppercase",
              mb: 1.5,
            }}
          >
            {t("home.principlesEyebrow")}
          </Typography>
          <Typography
            id={principlesId}
            variant="h2"
            sx={{ maxWidth: "26ch", mb: { xs: 4, md: 6 } }}
          >
            {t("home.principlesTitle")}
          </Typography>

          <Box
            sx={{
              display: "grid",
              gap: { xs: 2, md: 3 },
              gridTemplateColumns: { xs: "1fr", md: "repeat(3, 1fr)" },
            }}
          >
            {PRINCIPLES.map((number) => (
              <Card key={number} sx={{ height: "100%" }}>
                <CardContent sx={{ p: { xs: 2.5, md: 3.5 } }}>
                  <Typography
                    component="span"
                    sx={{
                      display: "block",
                      color: "primary.main",
                      fontVariantNumeric: "tabular-nums",
                      fontWeight: 680,
                      mb: 1.5,
                    }}
                  >
                    0{number}
                  </Typography>
                  <Typography variant="h3" sx={{ mb: 1 }}>
                    {t(`home.principle${number}Title`)}
                  </Typography>
                  <Typography color="text.secondary">
                    {t(`home.principle${number}Text`)}
                  </Typography>
                </CardContent>
              </Card>
            ))}
          </Box>
        </Box>
      </Container>

      <Box sx={{ borderTop: 1, borderColor: "divider" }}>
        <Container maxWidth="lg" sx={{ py: { xs: 7, md: 11 } }}>
          <Box
            component="section"
            aria-labelledby={roadmapId}
            sx={{
              display: "grid",
              gap: { xs: 2.5, md: 6 },
              gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" },
              alignItems: "start",
            }}
          >
            <Box>
              <Typography
                variant="body2"
                sx={{
                  color: "text.secondary",
                  fontWeight: 620,
                  letterSpacing: "0.08em",
                  textTransform: "uppercase",
                  mb: 1.5,
                }}
              >
                {t("home.roadmapEyebrow")}
              </Typography>
              <Typography id={roadmapId} variant="h2">
                {t("home.roadmapTitle")}
              </Typography>
            </Box>
            <Typography color="text.secondary" sx={{ maxWidth: "58ch" }}>
              {t("home.roadmapText")}
            </Typography>
          </Box>
        </Container>
      </Box>
    </Box>
  );
}
