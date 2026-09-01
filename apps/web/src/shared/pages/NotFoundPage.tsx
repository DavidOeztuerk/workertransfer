import Button from "@mui/material/Button";
import Container from "@mui/material/Container";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

/** Was bei einer unbekannten Adresse steht. */
export function NotFoundPage() {
  return (
    <Container maxWidth="sm" sx={{ py: { xs: 8, md: 14 }, textAlign: "center" }}>
      <Stack spacing={2} alignItems="center">
        <Typography variant="h1">Diese Seite gibt es nicht</Typography>
        <Typography color="text.secondary">
          Vielleicht wurde sie verschoben, vielleicht stimmt die Adresse nicht.
        </Typography>
        <Button component={RouterLink} to="/" variant="contained" sx={{ mt: 1 }}>
          Zur Startseite
        </Button>
      </Stack>
    </Container>
  );
}
