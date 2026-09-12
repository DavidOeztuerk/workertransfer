import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";

import { BERUFSFELDER, istBerufsfeld } from "../../lib/berufsfelder";
import type { BerufsfeldWahl } from "../../lib/berufsfelder";

/**
 * Die Auswahl des Berufsfelds (ADR-0039). Nie ein Pflichtfeld — der leere
 * Eintrag ist die Vorauswahl.
 */
export function BerufsfeldSelect({
  wert,
  onChange,
  disabled = false,
  label,
  helperText,
}: {
  wert: BerufsfeldWahl;
  onChange: (feld: BerufsfeldWahl) => void;
  disabled?: boolean;
  label?: string;
  helperText?: string;
}) {
  const { t } = useTranslation();

  return (
    <TextField
      select
      label={label ?? t("beruf.label")}
      helperText={helperText ?? t("beruf.hinweis")}
      value={wert ?? ""}
      disabled={disabled}
      onChange={(event) => {
        const gewaehlt = event.target.value;
        // Leer ist ein gültiger Wert und heisst „keins", nicht „unverändert".
        onChange(istBerufsfeld(gewaehlt) ? gewaehlt : null);
      }}
      slotProps={{ select: { displayEmpty: true } }}
    >
      <MenuItem value="">{t("beruf.keins")}</MenuItem>
      {BERUFSFELDER.map((feld) => (
        <MenuItem key={feld} value={feld}>
          {t(`beruf.feld.${feld}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
