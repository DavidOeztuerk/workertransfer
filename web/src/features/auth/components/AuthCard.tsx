import Box from "@mui/material/Box";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";

/**
 * Der Rahmen für Anmelden und Registrieren.
 *
 * Schmal und mittig: auf diesen beiden Seiten gibt es genau eine Aufgabe, und
 * eine volle Bildschirmbreite bietet nur Platz für Ablenkung.
 */
export function AuthCard({
  title,
  lead,
  children,
}: {
  title: string;
  lead?: string;
  children: React.ReactNode;
}) {
  return (
    <Container maxWidth="sm" sx={{ py: { xs: 5, md: 9 }, px: { xs: 2, sm: 3 } }}>
      <Box sx={{ mb: 3 }}>
        <Typography variant="h1" sx={{ mb: lead ? 1 : 0 }}>
          {title}
        </Typography>
        {lead ? (
          <Typography variant="body1" color="text.secondary">
            {lead}
          </Typography>
        ) : null}
      </Box>
      <Card>
        <CardContent sx={{ p: { xs: 2.5, sm: 4 } }}>{children}</CardContent>
      </Card>
    </Container>
  );
}
