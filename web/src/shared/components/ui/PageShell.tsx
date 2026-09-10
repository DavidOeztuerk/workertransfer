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
  /**
   * Eine Seite mit Formular oder Karten statt mit Liste oder Tabelle.
   *
   * Sie wird schmaler, aber NICHT halb so breit: der Unterschied soll ordnen,
   * nicht auffallen. Wer eine Tabelle zeigt, lässt es weg.
   */
  narrow?: boolean;
  children: React.ReactNode;
}) {
  return (
    <Container
      // AUSDRÜCKLICHE Breiten statt MUIs Rasterpunkten, und der Grund ist
      // gemessen: `sm` sind 600px, `lg` sind 1200px — auf einem 1280er Fenster
      // also 47% gegen 94%. Beim Blättern sprang der Rahmen dadurch auf die
      // doppelte Breite, und das liest sich wie zwei verschiedene Anwendungen.
      // Diese beiden Werte liegen ein Fünftel auseinander statt um das
      // Doppelte: der Rahmen bleibt beim Wechsel im Wesentlichen stehen.
      //
      // 960px ist kein runder Zufallswert, sondern die Breite, bei der ein
      // Formular zwei Spalten trägt, ohne dass eine Zeile Fliesstext länger
      // wird, als man sie noch lesen mag. Die Textspalte selbst ist unten
      // ohnehin auf 62ch begrenzt — 50 bis 75 Zeichen ist der Bereich, den die
      // Lesbarkeitsforschung nennt, und den hält keine Rahmenbreite allein ein.
      maxWidth={false}
      // Unten weniger als oben, und das ist Absicht: über dem Titel schafft
      // Luft den Anfang, unter dem letzten Element ist sie nur Leere vor der
      // Fusszeile.
      sx={{
        maxWidth: narrow ? 960 : 1160,
        pt: { xs: 4, md: 7 },
        pb: { xs: 3, md: 4 },
        px: { xs: 2, sm: 3, md: 4 },
      }}
    >
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
