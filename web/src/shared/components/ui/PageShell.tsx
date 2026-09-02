import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";

/**
 * Der Rahmen einer Inhaltsseite: Titel, ein erklärender Satz, der Inhalt.
 *
 * Der `lead` ist kein Zierstreifen. Diese Anwendung entscheidet über Freigaben,
 * und fast jede Seite muss sagen, WAS mit dem passiert, was jemand hier tut.
 * Ein fester Platz dafür sorgt dafür, dass der Satz nicht vergessen wird.
 */
export function PageShell({
  title,
  lead,
  actions,
  narrow = false,
  children,
}: {
  title: string;
  lead?: string;
  actions?: React.ReactNode;
  narrow?: boolean;
  children: React.ReactNode;
}) {
  return (
    <Container maxWidth={narrow ? "sm" : "lg"} sx={{ py: { xs: 3.5, md: 6 } }}>
      <Box
        sx={{
          display: "flex",
          flexDirection: { xs: "column", sm: "row" },
          justifyContent: "space-between",
          alignItems: { xs: "flex-start", sm: "flex-end" },
          gap: 2,
          mb: { xs: 3, md: 4 },
        }}
      >
        <Box sx={{ maxWidth: "62ch" }}>
          <Typography variant="h1" sx={{ mb: lead ? 1 : 0 }}>
            {title}
          </Typography>
          {lead ? (
            <Typography variant="body1" color="text.secondary">
              {lead}
            </Typography>
          ) : null}
        </Box>
        {actions ? <Box sx={{ flexShrink: 0 }}>{actions}</Box> : null}
      </Box>
      {children}
    </Container>
  );
}
