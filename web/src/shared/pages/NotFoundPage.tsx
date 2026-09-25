import Button from "@mui/material/Button";
import { useTranslation } from "react-i18next";
import Container from "@mui/material/Container";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

/** Was bei einer unbekannten Adresse steht. */
export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <Container maxWidth="sm" sx={{ py: { xs: 8, md: 14 }, textAlign: "center" }}>
      <Box sx={{ display: "flex", flexDirection: "column", gap: 2, alignItems: "center" }}>
        <Typography variant="h1">{t("notFound.title")}</Typography>
        <Typography color="text.secondary">
          {t("notFound.text")}
        </Typography>
        <Button component={RouterLink} to="/" variant="contained" sx={{ mt: 1 }}>
          {t("common.toHome")}
        </Button>
      </Box>
    </Container>
  );
}
