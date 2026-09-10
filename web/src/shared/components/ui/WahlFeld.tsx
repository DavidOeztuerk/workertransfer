import CheckIcon from "@mui/icons-material/Check";
import Box from "@mui/material/Box";
import ListItemIcon from "@mui/material/ListItemIcon";
import MenuItem from "@mui/material/MenuItem";
import Select from "@mui/material/Select";
import Typography from "@mui/material/Typography";
import type { ReactNode } from "react";

/** Eine Möglichkeit in einem <see cref="WahlFeld" />. */
export interface Wahlmoeglichkeit<T extends string> {
  wert: T;
  name: string;
  /** Ein Satz darunter, wenn der Name allein nicht sagt, was passiert. */
  hinweis?: string;
  symbol: ReactNode;
}

/** Eine benannte Wahl: Überschrift, erklärender Satz, Auswahlfeld. */
export function WahlFeld<T extends string>({
  ueberschrift,
  beschreibung,
  wert,
  moeglichkeiten,
  onWahl,
  etikett,
}: {
  ueberschrift: string;
  beschreibung: string;
  wert: T;
  moeglichkeiten: readonly Wahlmoeglichkeit<T>[];
  onWahl: (gewaehlt: T) => void;
  /** Für Vorleser: die Überschrift ist ein Text, kein Label. */
  etikett: string;
}) {
  const aktuell = moeglichkeiten.find((moeglich) => moeglich.wert === wert);

  return (
    <Box sx={{ display: "grid", gap: 1 }}>
      <Box>
        <Typography variant="subtitle2" sx={{ fontWeight: 660 }}>
          {ueberschrift}
        </Typography>
        <Typography variant="caption" color="text.secondary" component="div">
          {beschreibung}
        </Typography>
      </Box>

      <Select
        value={wert}
        size="small"
        fullWidth
        inputProps={{ "aria-label": etikett }}
        onChange={(event) => onWahl(event.target.value as T)}
        // Zugeklappt mit Symbol, wie in der Liste — `renderValue`, weil MUI
        // sonst nur den Text zeigt.
        renderValue={() => (
          <Box sx={{ display: "flex", alignItems: "center", gap: 1.25 }}>
            <Box sx={{ display: "flex", color: "text.secondary" }}>
              {aktuell?.symbol}
            </Box>
            {aktuell?.name ?? ""}
          </Box>
        )}
        sx={{ borderRadius: 2, "& .MuiSelect-select": { py: 1.1 } }}
      >
        {moeglichkeiten.map((moeglich) => (
          <MenuItem key={moeglich.wert} value={moeglich.wert} sx={{ py: 1 }}>
            <ListItemIcon sx={{ minWidth: 36, color: "text.secondary" }}>
              {moeglich.symbol}
            </ListItemIcon>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="body2">{moeglich.name}</Typography>
              {moeglich.hinweis ? (
                <Typography variant="caption" color="text.secondary" component="div">
                  {moeglich.hinweis}
                </Typography>
              ) : null}
            </Box>
            {moeglich.wert === wert ? (
              <CheckIcon fontSize="small" sx={{ ml: 1.5, color: "primary.main" }} />
            ) : null}
          </MenuItem>
        ))}
      </Select>
    </Box>
  );
}
