import Box from "@mui/material/Box";
import Pagination from "@mui/material/Pagination";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import useMediaQuery from "@mui/material/useMediaQuery";
import { useTheme } from "@mui/material/styles";
import { useTranslation } from "react-i18next";

/** Die Grössen, die zur Wahl stehen. Zwölf ist die Vorgabe. */
export const SEITENGROESSEN = [12, 24, 48, 96] as const;

/**
 * Die Blätterleiste: wo man ist, wie viele es sind, und wie viele pro Seite.
 *
 * <strong>Der Bereich steht links und nicht als „Seite 3 von 9".</strong>
 * „25–36 von 104" beantwortet zwei Fragen auf einmal — wo bin ich, und wie viel
 * kommt noch. Die Seitenzahl allein beantwortet keine davon, solange man die
 * Seitengrösse nicht im Kopf hat.
 *
 * <strong>Sie verschwindet nicht bei einer Seite.</strong> Die Wahl der
 * Seitengrösse bleibt sichtbar, denn genau dann will jemand sie vielleicht
 * verkleinern; nur die Nummernleiste selbst geht weg, weil sie nichts anbietet.
 */
export function PaginationControls({
  page,
  pageSize,
  totalItems,
  totalPages,
  onPage,
  onPageSize,
}: {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  onPage: (page: number) => void;
  onPageSize: (pageSize: number) => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const schmal = useMediaQuery(theme.breakpoints.down("sm"));

  const von = totalItems === 0 ? 0 : (page - 1) * pageSize + 1;
  const bis = Math.min(page * pageSize, totalItems);

  return (
    <Box
      sx={{
        display: "flex",
        flexDirection: { xs: "column", sm: "row" },
        alignItems: { xs: "stretch", sm: "center" },
        justifyContent: "space-between",
        gap: 2,
        mt: 4,
        pt: 3,
        borderTop: 1,
        borderColor: "divider",
      }}
    >
      <Typography variant="body2" color="text.secondary" role="status">
        {totalItems === 0
          ? t("blaettern.leer")
          : t("blaettern.bereich", { von, bis, gesamt: totalItems })}
      </Typography>

      <Box
        sx={{
          display: "flex",
          alignItems: "center",
          gap: 2,
          flexWrap: "wrap",
          justifyContent: { xs: "space-between", sm: "flex-end" },
        }}
      >
        {totalPages > 1 ? (
          <Pagination
            count={totalPages}
            page={page}
            onChange={(_event, ziel) => onPage(ziel)}
            // Auf schmalen Bildschirmen nur die Nachbarn: sonst bricht die
            // Leiste um und schiebt den Inhalt weg.
            siblingCount={schmal ? 0 : 1}
            boundaryCount={schmal ? 1 : 2}
            size={schmal ? "small" : "medium"}
            getItemAriaLabel={(typ, seite) =>
              typ === "previous"
                ? t("blaettern.vorige")
                : typ === "next"
                  ? t("blaettern.naechste")
                  : t("blaettern.seite", { nummer: seite })
            }
          />
        ) : null}

        <TextField
          select
          size="small"
          label={t("blaettern.proSeite")}
          value={String(pageSize)}
          onChange={(ereignis) => onPageSize(Number(ereignis.target.value))}
          // Nativ, wie jedes Auswahlfeld hier: MUIs Vorgabe ist ein Listenfeld
          // aus `div`s, das kein Screenreader als Auswahlfeld bedient.
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          sx={{ width: 104, flexShrink: 0 }}
        >
          {SEITENGROESSEN.map((groesse) => (
            <option key={groesse} value={groesse}>
              {groesse}
            </option>
          ))}
        </TextField>
      </Box>
    </Box>
  );
}
