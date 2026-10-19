import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";

/**
 * The frame of a content page: title, one explanatory sentence, the content.
 *
 * The `lead` is no decorative strip. This application decides about releases,
 * and almost every page has to say WHAT happens to what somebody does here. A
 * fixed place for it makes sure the sentence is not forgotten.
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
  /** A form or card page instead of a list/table: somewhat narrower. */
  narrow?: boolean;
  children: React.ReactNode;
}) {
  return (
    <Container
      // Widths of our own instead of MUI's `sm`/`lg` (600/1200): those lay a
      // factor of two apart, and the frame jumped while paging. These two are
      // a fifth apart. The text column below is limited to 62ch.
      maxWidth={false}
      // Less at the bottom than at the top, on purpose: above the title the
      // air makes the beginning, below the last element it is only emptiness
      // before the footer.
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
