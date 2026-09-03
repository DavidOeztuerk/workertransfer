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
 * Die drei Sätze, auf denen die Plattform steht — hier stehen nur ihre
 * Schlüssel, der Wortlaut liegt in den Katalogen.
 */
const grundlagen = [1, 2, 3] as const;

/**
 * Die Marketingseite auf `/` — und die Weiterleitung Angemeldeter auf
 * `/overview`.
 *
 * <strong>Zwei Adressen, nicht eine.</strong> Vorher bediente `/` beides:
 * abgemeldet die Werbung, angemeldet die Übersicht. Das hielt die Werbung zwar
 * aus dem Weg, gab der Übersicht aber keine eigene Adresse — sie war nicht
 * verlinkbar, und ein Screenshot von `/` zeigte je nach Sitzung etwas anderes.
 *
 * Solange die Sitzung noch unbekannt ist, wird NICHTS gezeichnet. Das ist der
 * Unterschied zwischen einer kurzen leeren Fläche und dem Sprung Werbung →
 * Übersicht, der nach einem Fehler aussieht.
 */
export function HomePage() {
  const { t } = useTranslation();
  const status = useAppSelector((zustand) => zustand.auth.status);

  const heldId = useId();
  const grundlagenId = useId();
  const wegId = useId();

  if (status === "authenticated") return <Navigate to="/overview" replace />;
  if (status === "unknown") return null;

  return (
    <Box>
      {/* Der Held. Grosszügig, ruhig, EIN Hauptaufruf — das hier ist das Erste,
          was jemand von dieser Plattform sieht. */}
      <Box
        component="section"
        aria-labelledby={heldId}
        sx={{
          position: "relative",
          overflow: "hidden",
          borderBottom: 1,
          borderColor: "divider",
          // Der Verlauf kommt aus der Palette, nicht aus Literalen: eine zweite
          // Farbquelle läuft beim Dunkelmodus auseinander.
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
              {t("start.auge")}
            </Typography>
            <Typography
              id={heldId}
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
              {t("start.titel")}
            </Typography>
            <Typography
              variant="body1"
              color="text.secondary"
              sx={{ fontSize: "1.0625rem", mb: 4 }}
            >
              {t("start.text")}
            </Typography>
            {/* Beide Wege führen nach /register, und der zweite trägt seine
                ABSICHT mit: ohne `?as=company` landet jemand, der „Als
                Unternehmen entdecken" klickt, im Personenformular — und merkt
                es erst nach der Bestätigungsmail, wenn kein Unternehmen da ist.

                Die Unterscheidung selbst trifft die Registrierung, nicht diese
                Seite: ein Unternehmen braucht eine BESTÄTIGTE Adresse
                (ADR-0019), und das Konto wäre hier noch pending. */}
            <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1.5 }}>
              <Button
                component={RouterLink}
                to="/register"
                variant="contained"
                size="large"
              >
                {t("start.alsPerson")}
              </Button>
              <Button
                component={RouterLink}
                to="/register?as=company"
                variant="outlined"
                size="large"
              >
                {t("start.alsUnternehmen")}
              </Button>
            </Box>
          </Box>
        </Container>
      </Box>

      <Container maxWidth="lg" sx={{ py: { xs: 7, md: 12 } }}>
        <Box component="section" aria-labelledby={grundlagenId}>
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
            {t("start.grundlagenAuge")}
          </Typography>
          <Typography
            id={grundlagenId}
            variant="h2"
            sx={{ maxWidth: "26ch", mb: { xs: 4, md: 6 } }}
          >
            {t("start.grundlagenTitel")}
          </Typography>

          <Box
            sx={{
              display: "grid",
              gap: { xs: 2, md: 3 },
              gridTemplateColumns: { xs: "1fr", md: "repeat(3, 1fr)" },
            }}
          >
            {grundlagen.map((nummer) => (
              <Card key={nummer} sx={{ height: "100%" }}>
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
                    0{nummer}
                  </Typography>
                  <Typography variant="h3" sx={{ mb: 1 }}>
                    {t(`start.grundlage${nummer}Titel`)}
                  </Typography>
                  <Typography color="text.secondary">
                    {t(`start.grundlage${nummer}Text`)}
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
            aria-labelledby={wegId}
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
                {t("start.wegAuge")}
              </Typography>
              <Typography id={wegId} variant="h2">
                {t("start.wegTitel")}
              </Typography>
            </Box>
            <Typography color="text.secondary" sx={{ maxWidth: "58ch" }}>
              {t("start.wegText")}
            </Typography>
          </Box>
        </Container>
      </Box>
    </Box>
  );
}
