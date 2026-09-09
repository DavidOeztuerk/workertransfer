import { useId } from "react";
import Box from "@mui/material/Box";
import MenuItem from "@mui/material/MenuItem";
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
 *
 * <strong>Zwei Gruppen, nicht drei Dinge nebeneinander.</strong> Links steht,
 * WO man ist — der Bereich und die Nummern gehören zusammen und werden auch so
 * gelesen. Rechts steht, WIE VIEL auf eine Seite kommt. Vorher trieb ein
 * <c>space-between</c> den Bereich ganz nach links, die Nummern in die Mitte
 * und die Grösse nach rechts: drei Inseln, deren Zusammenhang niemand sieht.
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
  const groessenFeld = useId();

  // NICHTS ZU BLAETTERN, ALSO KEINE LEISTE.
  //
  // Vorher stand hier „Nichts gefunden" — und daneben sagte der Leerblock
  // „Dazu wurde nichts gefunden." Zweimal dieselbe Auskunft untereinander, und
  // die E2E-Reise fiel darueber: zwei Treffer auf denselben Text. Der Leerblock
  // sagt es besser (er bietet auch gleich einen Ausweg an), also schweigt die
  // Leiste. Zu sortieren gibt es hier ohnehin nichts.
  if (totalItems === 0) {
    return null;
  }

  const von = (page - 1) * pageSize + 1;
  const bis = Math.min(page * pageSize, totalItems);

  return (
    <Box
      sx={{
        display: "flex",
        flexDirection: { xs: "column", sm: "row" },
        // `flex-start` und NICHT `stretch`: gestreckt füllt jede Gruppe die
        // ganze Breite, und ihr `space-between` riss dann „Pro Seite" und das
        // Feld an die gegenüberliegenden Ränder — zwei Dinge, die zusammen
        // gehören, so weit auseinander wie möglich.
        alignItems: { xs: "flex-start", sm: "center" },
        justifyContent: "space-between",
        gap: { xs: 1.5, sm: 2 },
        // Enger als vorher (mt 4 / pt 3). Die Leiste ist eine Fusszeile der
        // Liste, kein eigener Abschnitt — sie soll an ihr hängen.
        mt: 3,
        pt: 2,
        borderTop: 1,
        borderColor: "divider",
      }}
    >
      {/* LINKS: wo man ist. Bereich und Nummern sind EINE Aussage. */}
      <Box
        sx={{
          display: "flex",
          alignItems: "center",
          gap: { xs: 1.5, sm: 2 },
          flexWrap: "wrap",
        }}
      >
        <Typography
          variant="body2"
          color="text.secondary"
          role="status"
          sx={{ whiteSpace: "nowrap" }}
        >
          {t("blaettern.bereich", { von, bis, gesamt: totalItems })}
        </Typography>

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
      </Box>

      {/*
        RECHTS: wie viel auf eine Seite kommt — Beschriftung NEBEN dem Feld.

        Die Beschriftung steht als eigenes `label` daneben und nicht als
        `label`-Eigenschaft am Feld: die Oberfläche setzt Feldbeschriftungen
        grundsätzlich ÜBER das Feld (siehe `MuiInputLabel` im Thema), und hier
        wäre das eine zweizeilige Insel neben einer einzeiligen Leiste. Über
        `htmlFor` bleibt es trotzdem eine echte Beschriftung.
      */}
      <Box
        sx={{
          display: "flex",
          alignItems: "center",
          gap: 1,
          flexShrink: 0,
        }}
      >
        <Typography
          component="label"
          htmlFor={groessenFeld}
          variant="body2"
          color="text.secondary"
          sx={{ whiteSpace: "nowrap" }}
        >
          {t("blaettern.proSeite")}
        </Typography>

        <TextField
          select
          size="small"
          id={groessenFeld}
          value={String(pageSize)}
          onChange={(ereignis) => onPageSize(Number(ereignis.target.value))}
          sx={{ width: 88, flexShrink: 0 }}
        >
          {SEITENGROESSEN.map((groesse) => (
            <MenuItem key={groesse} value={groesse}>
              {groesse}
            </MenuItem>
          ))}
        </TextField>
      </Box>
    </Box>
  );
}
